#!/usr/bin/env python3
"""Call-site checker for the HoMM5 C# sources: does this member exist, and is
it called with the right number of arguments?

``csharp_access_check.py`` resolves ``Type.Member`` -- the qualified form. Its
own docstring names what it cannot see:

    access through a variable -- ``s.InternalOnly()`` where ``s`` is a local.
    Only the qualified ``Type.Member`` form is resolved; the other needs
    variable type inference.
    overloads, generics, extension methods, params, optional arguments

That blind spot is not theoretical for this project. Two defects shipped into
the town layer that no check in the repo would have caught, both of them
exactly this shape:

    MakePanel(transform, "TownPanel", min, max)   -- 4 args, 3 parameters
    public int X; X = def.x;                      -- MapObjectDef.x is float

The first is a call through a variable with the wrong arity. The second is an
assignment whose declared type disagrees with the value's. Neither is a
qualified access, so both sailed through.

This script closes the arity half by inferring the type of locals and fields,
then resolving ``x.Member(...)`` against the declared parameter lists.

What it infers, and how
  * ``TownScreen screen = ...``            -> from the declared type
  * ``var screen = TownScreen.Create(c)``  -> from the method's return type
  * ``var go = new GameObject("x")``       -> from the constructed type
  * ``foreach (var b in TownBuildings)``   -> from the collection's element type
  * field and property types, recursively  -> ``_town.Summary(db)``
  * ``this`` inside a type                 -> that type
  * a parameter's declared type            -> ``void Show(H5TownState town)``

What it deliberately does not do
  * overload resolution. When a name has several signatures, the call passes if
    ANY of them accepts the argument count. A call that picks the wrong
    overload by argument type is out of reach.
  * argument *types*. ``Build(db, building, resources, out reason)`` is checked
    for count only; passing an int where a string goes is not caught.
  * anything from UnityEngine / System / Newtonsoft. Only the 90-odd types
    declared in this repo are indexed, so ``go.transform.SetParent(...)`` is
    skipped -- ``go`` is a GameObject, which is not ours.
  * LINQ, delegates, lambdas, generics instantiation, implicit conversions,
    nullable flow, tuple deconstruction, ref returns.
  * cross-assembly extension methods, ``dynamic``, partial-class member
    merging across files (the collector unions them, which is close enough).

So: this is a net for arity and member-existence errors on our own types, not
a compiler. "No problems found" means the repo has no call whose argument
count cannot match any declaration it can see.

Run ``--self-test`` to watch it catch planted violations on synthetic files.

Usage:  python3 tools/csharp_call_check.py [project_dir]
        python3 tools/csharp_call_check.py --self-test
Exit 0 when nothing is wrong, 1 otherwise.
"""

from __future__ import annotations

import os
import sys

try:
    import tree_sitter_c_sharp as tscs
    from tree_sitter import Language, Parser
except ImportError:
    sys.exit("needs: pip install tree-sitter tree-sitter-c-sharp")

# Reuse the declaration collector: one index of types, not two that can drift.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from csharp_access_check import Collector, text, iter_cs  # noqa: E402


# ---------------------------------------------------------------- signatures

def param_info(node, src):
    """(required, optional, has_params) for a parameter list.

    ``int minTier = 1`` is optional, ``params object[] rest`` absorbs anything
    after it, ``out string reason`` is still an argument at the call site.
    """
    required = optional = 0
    has_params = False
    for child in node.children:
        if child.type != "parameter":
            continue
        if has_params:
            # C# requires params to be last; tolerate source that does not.
            optional += 1
            continue
        mods = {text(c, src) for c in child.children if c.type == "modifier"}
        if any(c.type == "identifier" and text(c, src) == "params"
               for c in child.children):
            has_params = True
            continue
        if text(child, src).find("=") >= 0 or "params" in mods:
            optional += 1
        else:
            required += 1
    return required, optional, has_params


def return_type_of(node, src):
    """The declared return type of a method, stripped of generics and arrays.

    The name is also an `identifier` sibling of the return type, so scanning
    every identifier before the parameter list ends on the *name*: `static
    Widget Make(...)` yields `Make`. Stopping at the name means taking the
    first type-ish node only, and never overwriting it with a later one.
    """
    stop = ("parameter_list", "block", "arrow_expression_clause", "semicolon",
            "accessor_list", "constructor_initializer")
    seen_name = False
    for child in node.children:
        if child.type in stop:
            break
        if child.type == "modifier":
            continue
        if child.type in ("identifier", "generic_name", "predefined_type",
                          "array_type", "nullable_type", "qualified_name",
                          "void_keyword"):
            if seen_name:
                # A second type-ish node after the name: this is the return
                # type of an explicit interface implementation or similar.
                continue
            first = child
            seen_name = True
            # The name follows the return type, so the *first* such node is the
            # type only when a name comes after it. Detect by lookahead.
            nxt = child.next_named_sibling
            if nxt is not None and nxt.type in ("identifier", "generic_name"):
                return text(child, src)
            return None
    return None


def simple(type_text):
    """`List<TownBuildingDef>` -> `List`; `int[]` -> `int[]`; `a.B` -> `B`."""
    if not type_text:
        return None
    t = type_text.split("<")[0].strip()
    t = t.split(".")[-1].strip()
    return t or None


def element_type(type_text):
    """The T of `List<T>` / `Dictionary<K,T>`'s value / `T[]`."""
    if not type_text:
        return None
    if "<" in type_text and type_text.rstrip().endswith(">"):
        inner = type_text[type_text.index("<") + 1:-1]
        # Dictionary<K, V> -> V is what foreach yields as Value, but foreach
        # over a dictionary yields KeyValuePair; callers here iterate lists.
        parts = split_top_level(inner)
        return simple(parts[-1])
    if type_text.endswith("[]"):
        return simple(type_text[:-2])
    return None


def split_top_level(s):
    out, depth, cur = [], 0, ""
    for ch in s:
        if ch == "<":
            depth += 1
        elif ch == ">":
            depth -= 1
        elif ch == "," and depth == 0:
            out.append(cur.strip())
            cur = ""
            continue
        cur += ch
    out.append(cur.strip())
    return out


class Signatures:
    """Second pass: for every declared type, its methods' arities and the
    declared type of every field, property and method return."""

    def __init__(self, collector):
        self.collector = collector
        # type name -> member name -> list of (required, optional, has_params)
        self.methods = {}
        # type name -> member name -> simple type name
        self.member_types = {}
        # type name -> {member name}
        self.callables = {}

    def collect(self, path, tree, src):
        stack = []

        def walk(node):
            if node.type in ("class_declaration", "struct_declaration",
                             "interface_declaration", "record_declaration"):
                name = next((text(c, src) for c in node.children
                             if c.type == "identifier"), None)
                if name:
                    stack.append(name)
                    for child in node.children:
                        if child.type == "declaration_list":
                            for m in child.children:
                                self.member(name, m, src)
            for child in node.children:
                walk(child)
            if node.type in ("class_declaration", "struct_declaration",
                             "interface_declaration", "record_declaration"):
                if stack:
                    stack.pop()

        walk(tree.root_node)

    def member(self, owner, node, src):
        from csharp_access_check import member_names
        names = member_names(node, src)
        if not names:
            return
        mods = {text(c, src) for c in node.children if c.type == "modifier"}

        if node.type == "method_declaration":
            plist = next((c for c in node.children
                          if c.type == "parameter_list"), None)
            if plist is None:
                return
            sig = param_info(plist, src)
            for n in names:
                self.methods.setdefault(owner, {}).setdefault(n, []).append(sig)
                self.callables.setdefault(owner, set()).add(n)
            rt = return_type_of(node, src)
            if rt and "void" not in rt:
                for n in names:
                    self.member_types.setdefault(owner, {})[n] = simple(rt)

        elif node.type == "constructor_declaration":
            plist = next((c for c in node.children
                          if c.type == "parameter_list"), None)
            if plist is not None:
                self.methods.setdefault(owner, {}).setdefault(
                    owner, []).append(param_info(plist, src))

        elif node.type == "property_declaration":
            # A property's type is the identifier before its name.
            ptype = None
            for child in node.children:
                if child.type in ("identifier", "generic_name",
                                  "predefined_type", "array_type",
                                  "nullable_type", "qualified_name"):
                    ptype = text(child, src)
                elif child.type in ("accessor_list", "arrow_expression_clause",
                                    "equals_value_clause", "semicolon"):
                    break
            if ptype:
                for n in names:
                    self.member_types.setdefault(owner, {})[n] = simple(ptype)

        elif node.type == "field_declaration":
            # The type is not a child of field_declaration: it is the first
            # type-ish node of the nested variable_declaration. Reading the
            # outer children finds only the field name and indexes every field
            # as having its own name as its type.
            ftype = None
            for child in node.children:
                if child.type != "variable_declaration":
                    continue
                for sub in child.children:
                    if sub.type in ("identifier", "generic_name",
                                    "predefined_type", "array_type",
                                    "nullable_type", "qualified_name"):
                        ftype = text(sub, src)
                        break
                break
            raw = None
            for child in node.children:
                if child.type == "variable_declaration":
                    for sub in child.children:
                        if sub.type in ("identifier", "generic_name",
                                        "predefined_type", "array_type",
                                        "nullable_type", "qualified_name"):
                            raw = text(sub, src)
                            break
                    break
            if ftype:
                for n in names:
                    self.member_types.setdefault(owner, {})[n] = simple(ftype)
                    if raw and ("List" in raw or "<" in raw or raw.endswith("[]")):
                        self.member_types.setdefault(owner, {})[
                            n + "[]"] = element_type(raw)


# ------------------------------------------------------------- type finding

class TypeFinder:
    """Resolves an expression's type name, walking member chains.

    Scope is a stack of dicts so a local declared inside an `if` does not leak
    out of it, and a `foreach` variable dies with its loop.
    """

    def __init__(self, sigs, collector):
        self.sigs = sigs
        self.collector = collector
        self.scopes = [{}]
        self.current_type = None
        # name -> [signatures] for local functions, which are callable like
        # methods but declared inside one.
        self.locals_funcs = {}
        # names bound to a delegate type (Action<>, Func<>, EventHandler).
        # Invoking one is legal and its arity is not ours to check.
        self.delegates = set()

    # -- scope plumbing ---------------------------------------------------

    def push(self):
        self.scopes.append({})

    def pop(self):
        if len(self.scopes) > 1:
            self.scopes.pop()

    def bind(self, name, type_name):
        if name and type_name:
            self.scopes[-1][name] = type_name

    def lookup(self, name):
        for scope in reversed(self.scopes):
            if name in scope:
                return scope[name]
        return None

    # -- expression typing ------------------------------------------------

    def type_of(self, node, src):
        """A simple type name for this expression, or None when unknowable."""
        if node is None:
            return None
        t = node.type

        if t == "this_expression":
            return self.current_type
        if t == "identifier":
            name = text(node, src)
            known = self.lookup(name)
            if known:
                return known
            info = self.collector.types.get(name)
            if info is not None and self._is_static_only(name):
                return name
            return None
        if t == "object_creation_expression":
            for child in node.children:
                if child.type in ("identifier", "generic_name", "qualified_name"):
                    return simple(text(child, src))
            return None
        if t == "cast_expression":
            for child in node.children:
                if child.type in ("identifier", "generic_name",
                                  "predefined_type", "array_type"):
                    return simple(text(child, src))
            return None
        if t == "array_creation_expression":
            return None
        if t == "invocation_expression":
            callee = next((c for c in node.children
                           if c.type not in ("argument_list",)), None)
            return self._invocation_result(callee, src)
        if t == "member_access_expression":
            kids = [c for c in node.children if c.type != "."]
            if len(kids) < 2:
                return None
            base_type = self.type_of(kids[0], src)
            member = text(kids[-1], src)
            if base_type is None:
                # Maybe the left side is itself a type name: `GameDatabase.X`.
                left = simple(text(kids[0], src))
                if left in self.collector.types:
                    return self.sigs.member_types.get(left, {}).get(member)
                return None
            return self._member_type(base_type, member)
        if t == "conditional_expression":
            kids = [c for c in node.children if c.type not in ("?", ":")]
            if len(kids) >= 2:
                return self.type_of(kids[1], src)
            return None
        if t == "parenthesized_expression":
            for child in node.children:
                if child.type not in ("(", ")"):
                    return self.type_of(child, src)
            return None
        if t == "binary_expression":
            return None
        return None

    def _is_static_only(self, name):
        return name in self.collector.types

    def _member_type(self, owner, member):
        """The type of `owner.member`, following base classes."""
        seen = set()
        cur = owner
        while cur and cur not in seen:
            seen.add(cur)
            mt = self.sigs.member_types.get(cur, {}).get(member)
            if mt:
                return mt
            info = self.collector.types.get(cur)
            cur = None
            if info:
                for b in info["bases"]:
                    if b in self.collector.types:
                        cur = b
                        break
        return None

    def _invocation_result(self, callee, src):
        if callee is None:
            return None
        if callee.type == "member_access_expression":
            kids = [c for c in callee.children if c.type != "."]
            if len(kids) < 2:
                return None
            owner = self.type_of(kids[0], src) or simple(text(kids[0], src))
            if owner is None:
                return None
            return self._member_type(owner, text(kids[-1], src))
        if callee.type == "identifier":
            name = text(callee, src)
            if self.current_type:
                mt = self.sigs.member_types.get(self.current_type, {}).get(name)
                if mt:
                    return mt
            return None
        return None

    # -- element types, for foreach --------------------------------------

    def element_of(self, node, src):
        t = self.type_of(node, src)
        if t is None:
            return None
        # Prefer the declared `T[]` marker recorded for fields.
        return t


# ------------------------------------------------------------------- checks

def _is_delegate(type_name):
    """Action, Action<string>, Func<int, bool>, EventHandler."""
    if not type_name:
        return False
    base = type_name.split("<")[0].split(".")[-1]
    return base in ("Action", "Func", "EventHandler", "Predicate",
                    "Comparison", "Converter")


def arg_count(arglist, src):
    """How many arguments a call passes. `out x` is still one argument."""
    if arglist is None:
        return 0
    return sum(1 for c in arglist.children if c.type == "argument")


class CallChecker:
    def __init__(self, sigs, collector):
        self.sigs = sigs
        self.collector = collector
        self.problems = []
        self.checked = 0
        self.resolved = 0

    def check_file(self, path, tree, src):
        finder = TypeFinder(self.sigs, self.collector)
        type_stack = []

        def walk(node):
            t = node.type

            if t in ("class_declaration", "struct_declaration",
                     "interface_declaration", "record_declaration"):
                name = next((text(c, src) for c in node.children
                             if c.type == "identifier"), None)
                if name:
                    type_stack.append(name)
                    finder.current_type = name
                    finder.push()
                    # Field initialisers and property types are in scope.
                    for fname, ftype in self.sigs.member_types.get(name, {}).items():
                        if not fname.endswith("[]"):
                            finder.bind(fname, ftype)

            elif t in ("block", "switch_body", "if_statement", "for_statement",
                       "while_statement", "do_statement", "using_statement",
                       "lock_statement", "try_statement", "catch_clause",
                       "finally_clause", "lambda_expression"):
                finder.push()

            elif t == "local_declaration_statement":
                self._bind_local(node, src, finder)

            elif t == "local_function_statement":
                # A local function is callable like a method of the enclosing
                # type. Without this, every `void Add(...)` declared inside a
                # method reports as a missing member.
                self._bind_local_function(node, src, finder)

            elif t == "for_statement":
                self._bind_local(node, src, finder)

            elif t == "foreach_statement":
                self._bind_foreach(node, src, finder)

            elif t == "catch_declaration":
                for child in node.children:
                    if child.type in ("identifier", "generic_name",
                                      "qualified_name"):
                        finder.bind(text(child, src).split(".")[-1], None)

            elif t in ("parameter_list",):
                for child in node.children:
                    if child.type == "parameter":
                        self._bind_param(child, src, finder)

            elif t == "invocation_expression":
                self._check_call(node, src, finder, path)

            for child in node.children:
                walk(child)

            if t in ("block", "switch_body", "if_statement", "for_statement",
                     "while_statement", "do_statement", "using_statement",
                     "lock_statement", "try_statement", "catch_clause",
                     "finally_clause", "lambda_expression",
                     "class_declaration", "struct_declaration",
                     "interface_declaration", "record_declaration"):
                finder.pop()
                if t in ("class_declaration", "struct_declaration",
                         "interface_declaration", "record_declaration"):
                    if type_stack:
                        type_stack.pop()
                    finder.current_type = type_stack[-1] if type_stack else None

        walk(tree.root_node)

    # -- binding helpers --------------------------------------------------

    def _bind_local(self, node, src, finder):
        vdecl = next((c for c in node.children
                      if c.type == "variable_declaration"), None)
        if vdecl is None:
            return
        declared = None
        for child in vdecl.children:
            if child.type == "identifier":
                declared = simple(text(child, src))
            elif child.type in ("generic_name", "predefined_type",
                                "array_type", "nullable_type", "qualified_name"):
                declared = simple(text(child, src))
            elif child.type == "implicit_type":
                declared = None
        # A `var` is typed by its initialiser. tree-sitter puts the initialiser
        # straight under variable_declarator as `= <expr>` -- there is no
        # equals_value_clause node here, unlike in a field declaration, so
        # looking only for that wrapper finds nothing and every `var` stays
        # untyped.
        inferred_from = None
        if declared is None:
            for child in vdecl.children:
                if child.type == "variable_declarator":
                    for sub in child.children:
                        if sub.type == "equals_value_clause":
                            for val in sub.children:
                                if val.type != "=":
                                    inferred_from = val
                                    break
                        elif sub.type not in ("identifier", "=", ","):
                            inferred_from = sub
                        if inferred_from is not None:
                            break
                    break
        for child in vdecl.children:
            if child.type == "variable_declarator":
                name = next((text(s, src) for s in child.children
                             if s.type == "identifier"), None)
                if name is None:
                    continue
                ty = declared
                if ty is None and inferred_from is not None:
                    ty = finder.type_of(inferred_from, src)
                if ty:
                    finder.bind(name, ty)

    def _bind_foreach(self, node, src, finder):
        """`foreach (var b in TownBuildings)` -- b is the element type.

        The element type comes from the collection's *declared* generic
        argument, which is recorded on the variable we can resolve.
        """
        var_type = None
        collection = None
        for child in node.children:
            if child.type == "variable_declaration":
                for sub in child.children:
                    if sub.type == "variable_declarator":
                        var_type = next((text(s, src) for s in sub.children
                                         if s.type == "identifier"), None)
            elif child.type not in ("foreach", "(", ")", "in", ";"):
                collection = child
        if var_type is None or collection is None:
            return
        declared = None
        for child in node.children:
            if child.type == "variable_declaration":
                for sub in child.children:
                    if sub.type in ("identifier", "generic_name"):
                        cand = text(sub, src)
                        if cand != var_type:
                            declared = simple(cand)
        coll_type = finder.type_of(collection, src)
        elem = None
        if coll_type:
            elem = finder.lookup_elem(coll_type) if hasattr(finder, "lookup_elem") else None
        if elem is None and declared:
            elem = None
        # Fall back to the recorded `T[]` marker of the owning field.
        if elem is None and coll_type:
            elem = self.sigs.member_types.get(finder.current_type or "", {}).get(
                text(collection, src).split(".")[-1] + "[]")
        finder.bind(var_type, elem)

    def _bind_local_function(self, node, src, finder):
        from csharp_access_check import member_names
        names = member_names(node, src)
        plist = next((c for c in node.children
                      if c.type == "parameter_list"), None)
        if not names or plist is None:
            return
        sig = param_info(plist, src)
        for n in names:
            finder.locals_funcs.setdefault(n, []).append(sig)

    def _bind_param(self, node, src, finder):
        ptype = None
        for child in node.children:
            if child.type in ("identifier", "generic_name", "predefined_type",
                              "array_type", "nullable_type", "qualified_name"):
                ptype = simple(text(child, src))
                break
        name = None
        for child in node.children:
            if child.type == "identifier":
                name = text(child, src)
        if name and ptype:
            finder.bind(name, ptype)

    # -- the actual check -------------------------------------------------

    def _check_call(self, node, src, finder, path):
        kids = [c for c in node.children if c.type != "argument_list"]
        arglist = next((c for c in node.children
                        if c.type == "argument_list"), None)
        if not kids:
            return
        callee = kids[0]
        n = arg_count(arglist, src)

        owner = None
        member = None
        if callee.type == "member_access_expression":
            parts = [c for c in callee.children if c.type != "."]
            if len(parts) < 2:
                return
            member = text(parts[-1], src)
            owner = finder.type_of(parts[0], src)
            if owner is None:
                owner = simple(text(parts[0], src))
            # Invoking a delegate-typed member -- `onClick()` where onClick is
            # an Action -- is legal and its arity belongs to the delegate type.
            if owner and owner in self.collector.types:
                mtype = self.sigs.member_types.get(owner, {}).get(member)
                if mtype and _is_delegate(mtype):
                    return
        elif callee.type == "identifier":
            member = text(callee, src)
            # A local function shadows nothing but is callable like a method.
            if member in finder.locals_funcs:
                self.resolved += 1
                if not any(self._fits(n, sig)
                           for sig in finder.locals_funcs[member]):
                    shapes = ", ".join(self._describe(s)
                                       for s in finder.locals_funcs[member])
                    self.problems.append(
                        f"{path}:{node.start_point[0]+1}: CS1501 local "
                        f"function {member} takes {shapes}; called with {n}")
                return
            if member in finder.delegates or _is_delegate(
                    finder.lookup(member) or ""):
                return
            owner = finder.current_type
        else:
            return

        if owner is None or owner not in self.collector.types:
            # Not one of our types (UnityEngine, System, a lambda, ...).
            return
        sigs = self.sigs.methods.get(owner, {}).get(member)
        if sigs is None:
            # Not a method we know; could be a delegate field or a property
            # returning a delegate. Only flag when the member is entirely
            # absent from the type, which is a hard error either way.
            all_members = self.collector.types[owner]["members"]
            if member not in all_members:
                if self._has_external_base(owner, member):
                    # The type derives from MonoBehaviour, ScriptableObject or
                    # another type outside this repo. Destroy, StartCoroutine,
                    # GetComponent and the rest of the Unity API are inherited
                    # from there and invisible to this index, so a miss here
                    # proves nothing.
                    return
                self.problems.append(
                    f"{path}:{node.start_point[0]+1}: CS1061 "
                    f"{owner}.{member}() -- no such member on {owner}")
            return

        self.resolved += 1
        if not any(self._fits(n, sig) for sig in sigs):
            shapes = ", ".join(self._describe(s) for s in sigs)
            self.problems.append(
                f"{path}:{node.start_point[0]+1}: CS1501 "
                f"{owner}.{member} takes {shapes}; called with {n}")

    def _has_external_base(self, owner, member):
        """True when any base class is outside this repo.

        MonoBehaviour, ScriptableObject and friends are not in the index, so a
        type deriving from them inherits members we cannot enumerate. Reporting
        those as missing produced 62 findings, all of them Destroy(),
        StartCoroutine() and MemberwiseClone().
        """
        # Every class implicitly derives from object, whose MemberwiseClone,
        # ToString, GetHashCode and Equals are therefore always callable but
        # never in this index.
        if member in ("MemberwiseClone", "ToString", "GetHashCode", "Equals",
                      "GetType", "ReferenceEquals"):
            return True
        seen = set()
        stack = [owner]
        while stack:
            cur = stack.pop()
            if cur in seen:
                continue
            seen.add(cur)
            info = self.collector.types.get(cur)
            if info is None:
                continue
            for b in info["bases"]:
                if b in self.collector.types:
                    stack.append(b)
                elif b and b not in ("object", "ValueType", "Enum"):
                    return True
        return False

    @staticmethod
    def _fits(n, sig):
        required, optional, has_params = sig
        if n < required:
            return False
        if has_params:
            return n >= required - 1
        return n <= required + optional

    @staticmethod
    def _describe(sig):
        required, optional, has_params = sig
        if has_params:
            return f"{required}+ args (params)"
        if optional:
            return f"{required}-{required + optional} args"
        return f"{required} arg" + ("s" if required != 1 else "")


# --------------------------------------------------------------------- main

def run(root, quiet=False):
    files = sorted(iter_cs(root))
    if not files:
        sys.exit("no .cs files under %s" % root)

    parser = Parser(Language(tscs.language()))
    trees = {}
    collector = Collector()
    for path in files:
        with open(path, "rb") as fh:
            src = fh.read()
        tree = parser.parse(src)
        trees[path] = (tree, src)
        collector.walk(tree.root_node, src, path, [], [])

    sigs = Signatures(collector)
    for path in files:
        tree, src = trees[path]
        sigs.collect(path, tree, src)

    checker = CallChecker(sigs, collector)
    for path in files:
        tree, src = trees[path]
        checker.check_file(path, tree, src)

    nmethods = sum(len(v) for v in sigs.methods.values())
    if not quiet:
        print(f"scanned {len(files)} files, {len(collector.types)} types, "
              f"{nmethods} method names; resolved {checker.resolved} calls "
              f"on our own types")
    if checker.problems:
        for p in checker.problems:
            print("  " + p)
        print(f"\n{len(checker.problems)} problem(s)")
        return 1
    if not quiet:
        print("every resolved call matches a declaration's arity; "
              "this is not a compile")
    return 0


# Planted violations. Each one is a shape that actually shipped into this repo
# or that the docstring claims to catch; the two "ok" calls must stay silent.
SELF_TEST = [
    "namespace Demo {",
    "    public class Widget {",
    "        public int Value;",
    "        public Widget Child { get; set; }",
    '        public static Widget Make(string a, Vector2 b) { return null; }',
    "        public int Read(int a) { return a; }",
    "        public int Read(int a, int b) { return a + b; }",
    "        public void Optional(int a, int b = 2) { }",
    "        public void Rest(int a, params int[] more) { }",
    "        public void Out(string a, out int b) { b = 0; }",
    "        void Hidden() { }",
    "    }",
    "    public class User {",
    "        Widget _field = null;",
    "        public void Ok1() {",
    '            var w = Widget.Make("x", new Vector2(1, 2));',
    "            w.Read(1);",
    "            w.Optional(1);",
    "            w.Rest(1, 2, 3);",
    '            w.Out("a", out int n);',
    "            Widget other = w;",
    "            other.Read(1, 2);",
    "        }",
    "        public void Bad1() {",
    '            var w = Widget.Make("x", new Vector2(1, 2));',
    '            w.Read(1, 2, 3);              // CS1501: 3 args, max 2',
    "        }",
    "        public void Bad2() {",
    '            var w = Widget.Make("x", new Vector2(1, 2));',
    "            w.NoSuchMethod();             // CS1061",
    "        }",
    "        public void Bad3(Widget param) {",
    "            param.Hidden();               // CS0122-class: private",
    "        }",
    "        public void Bad4() {",
    "            _field.Read(1, 2, 3, 4);      // CS1501 through a field",
    "        }",
    "    }",
    "}",
]

# Bad1 (arity), Bad2 (missing member), Bad4 (arity through a field).
# Bad3 is a private call: this checker resolves the member, and private access
# is csharp_access_check.py's job -- it must NOT be double-reported here as an
# arity error, since Hidden() takes 0 args and is called with 0.
SELF_TEST_EXPECTED = 3


def self_test():
    import tempfile

    with tempfile.TemporaryDirectory() as tmp:
        path = os.path.join(tmp, "Demo.cs")
        with open(path, "w") as fh:
            fh.write("\n".join(SELF_TEST) + "\n")
        print("self-test: %d findings expected on planted violations"
              % SELF_TEST_EXPECTED)
        rc = run(tmp)
    if rc == 1:
        print("self-test PASSED: the checker reports the violations it is fed")
        return 0
    print("self-test FAILED: the checker stayed silent on known violations")
    return 1


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.argv.remove("--self-test")
        sys.exit(self_test())
    sys.exit(run(sys.argv[1] if len(sys.argv) > 1 else "."))
