#!/usr/bin/env python3
"""Flag simple names that a C# member uses but nothing declares.

csharp_call_check.py and csharp_access_check.py cover call arity and qualified
member access. Neither sees an identifier that was never declared at all -- a
local used outside its method, a parameter from a neighbouring method -- which
is CS0103 and is easy to write without a compiler. This checks it per member:

  * a lowercase simple name used inside a method, property, constructor or
    field initialiser must be declared inside that member (parameter, local,
    foreach/catch/pattern variable, lambda parameter, local function), or be a
    member name declared somewhere in the project, or be a known inherited
    MonoBehaviour member.

It is a heuristic, not a compiler: it does not resolve types or namespaces, and
it only looks at lowercase names. Every hit should be read; a clean run means
no undefined lowercase names it can see.

Usage: python3 tools/csharp_name_check.py [root]     (default: UnityProject/Assets/Scripts)
"""
import pathlib
import sys

from tree_sitter import Language, Parser
import tree_sitter_c_sharp as tcs

PARSER = Parser(Language(tcs.language()))

# Simple names a MonoBehaviour / Object subclass can use without declaring them.
INHERITED = {
    "transform", "gameObject", "enabled", "name", "tag", "hideFlags",
    "isActiveAndEnabled", "useGUILayout", "runInEditMode", "value",
    "nameof", "var", "dynamic", "args", "await",
}

MEMBER_KINDS = {
    "method_declaration", "constructor_declaration", "property_declaration",
    "accessor_declaration", "operator_declaration", "indexer_declaration",
    "field_declaration", "event_field_declaration",
}
# Local functions are not listed: they see the enclosing member's locals, so
# they are checked as part of that member.


def text(src, n):
    return src[n.start_byte:n.end_byte].decode("utf8", "replace")


def walk(n):
    stack = [n]
    while stack:
        x = stack.pop()
        yield x
        stack.extend(reversed(x.children))


def declared_name_nodes(n):
    """Identifier nodes that declare a name, by their syntactic position."""
    p = n.parent
    if p is None:
        return False
    t = p.type
    if t == "variable_declarator":
        return n == p.children[0]
    if t == "parameter":
        # The name is the identifier right after the type (modifiers aside).
        # A default value may be parsed as a further identifier, so not the last.
        named = [c for c in p.children if c.is_named and c.type not in ("modifier", "attribute_list")]
        if len(named) == 1:                  # (a, b) => ...  untyped lambda parameter
            return named[0].type == "identifier" and n == named[0]
        return len(named) >= 2 and named[1].type == "identifier" and n == named[1]
    if t == "foreach_statement":
        # foreach (<type> name in expr): the name is the identifier just before `in`.
        kids = p.children
        for i, c in enumerate(kids):
            if c.type == "in" and i > 0:
                return n == kids[i - 1]
        return False
    if t in ("declaration_expression", "tuple_pattern", "variable_designation", "lambda_expression"):
        # out var x; var (a, b); x => ...   (lambda: the parameter is a direct child)
        if t == "lambda_expression":
            return n == p.children[0]
        return True
    if t == "catch_declaration":
        # catch (Type name): the name is the last identifier (the ')' is a child too).
        direct = [c for c in p.children if c.type == "identifier"]
        return bool(direct) and n == direct[-1]
    if t == "declaration_pattern":
        direct = [c for c in p.children if c.type == "identifier"]
        return bool(direct) and n == direct[-1]
    if t == "tuple_element":
        direct = [c for c in p.children if c.type == "identifier"]
        return bool(direct) and n == direct[-1]
    if t == "parameter_list":
        # `params T[] name` puts the name directly under parameter_list.
        return True
    if t in ("local_function_statement", "method_declaration", "class_declaration",
             "struct_declaration", "enum_declaration", "interface_declaration",
             "property_declaration", "enum_member_declaration", "constructor_declaration",
             "event_field_declaration", "namespace_declaration"):
        return n == next((c for c in p.children if c.type == "identifier"), None)
    if t == "implicit_parameter":
        return True
    return False


def is_non_value_use(n):
    """Identifiers that are not references to a variable or member at all."""
    p = n.parent
    if p is None:
        return True
    t = p.type
    if t == "member_access_expression" and p.children and n == p.children[-1]:
        return True                       # a.b -> b is a member name
    if t == "argument" and n.next_sibling is not None and n.next_sibling.type == ":":
        return True                       # named argument
    if t == "assignment_expression" and n == p.children[0] and \
            p.parent is not None and p.parent.type == "initializer_expression":
        return True                       # object initialiser member
    if t in ("generic_name", "type_argument_list", "qualified_name", "object_creation_expression",
             "base_list", "cast_expression", "array_type", "nullable_type", "type_parameter",
             "type_parameter_constraint", "using_directive", "namespace_declaration",
             "attribute", "attribute_argument", "predefined_type"):
        return True                       # type / namespace / attribute positions
    if t == "variable_declaration" and n == p.children[0]:
        return True                       # declared type
    if t == "parameter" and n != p.children[-1]:
        return True                       # parameter type
    if t == "declaration_pattern" and n == p.children[0]:
        return True                       # pattern type
    if t == "catch_declaration":
        return True
    return False


def collect_members(src, root, members, fields_ok=True):
    """Project-wide: every name declared at type level (types, members, enum values)."""
    for n in walk(root):
        t = n.type
        if t in ("class_declaration", "struct_declaration", "interface_declaration",
                 "enum_declaration", "record_declaration", "delegate_declaration"):
            ident = next((c for c in n.children if c.type == "identifier"), None)
            if ident is not None:
                members.add(text(src, ident))
        elif t in ("method_declaration", "property_declaration", "constructor_declaration",
                   "event_field_declaration", "enum_member_declaration", "indexer_declaration"):
            ident = next((c for c in n.children if c.type == "identifier"), None)
            if ident is not None:
                members.add(text(src, ident))
        elif t == "variable_declarator":
            # Fields: a variable_declaration whose grandparent is a declaration_list.
            vd = n.parent
            if vd is not None and vd.type == "variable_declaration" and vd.parent is not None \
                    and vd.parent.type == "field_declaration":
                members.add(text(src, n.children[0]))
        elif t == "enum_member_declaration":
            members.add(text(src, n.children[0]))


def check_member(src, path, member, findings):
    declared = set()
    for n in walk(member):
        if n.type == "identifier" and declared_name_nodes(n):
            declared.add(text(src, n))
        elif n.type == "identifier" and n.parent is not None and n.parent.type == "local_function_statement":
            declared.add(text(src, n))
    return declared


def check_file(path, members, problems):
    src = path.read_bytes()
    tree = PARSER.parse(src)
    for member in iter_members(tree.root_node):
        declared = set()
        for n in walk(member):
            if n.type == "identifier" and declared_name_nodes(n):
                declared.add(text(src, n))
            elif n.type == "implicit_parameter":      # x => ... (a leaf node)
                declared.add(text(src, n))
        for n in walk(member):
            if n.type != "identifier":
                continue
            name = text(src, n)
            if not name or name == "_" or not (name[0].islower() or name[0] == "_"):
                continue
            if declared_name_nodes(n) or is_non_value_use(n):
                continue
            if name in declared or name in members or name in INHERITED:
                continue
            problems.append((str(path), n.start_point[0] + 1, name))


def iter_members(root):
    """Outermost member bodies: each method/property/ctor/field initialiser, once."""
    for n in walk(root):
        if n.type in MEMBER_KINDS:
            yield n


def run(root):
    base = pathlib.Path(root)
    files = sorted(base.rglob("*.cs"))
    if not files:
        print(f"no .cs files under {root}")
        return 2
    members = set()
    for f in files:
        src = f.read_bytes()
        collect_members(src, PARSER.parse(src).root_node, members)
    problems = []
    for f in files:
        check_file(f, members, problems)
    # Members nested in members are visited twice; de-duplicate by location.
    seen = set()
    unique = []
    for p in problems:
        if (p[0], p[1], p[2]) not in seen:
            seen.add((p[0], p[1], p[2]))
            unique.append(p)
    for path, line, name in unique:
        print(f"{path}:{line}: '{name}' is used but never declared in its scope")
    print(f"scanned {len(files)} files, {len(members)} project member names; {len(unique)} undeclared name(s)")
    return 1 if unique else 0


if __name__ == "__main__":
    sys.exit(run(sys.argv[1] if len(sys.argv) > 1 else "UnityProject/Assets/Scripts"))
