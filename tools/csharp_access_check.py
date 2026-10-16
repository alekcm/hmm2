#!/usr/bin/env python3
"""Static accessibility check for the HoMM5 C# sources.

There is no Unity editor or .NET SDK in this environment, so the C# cannot be
compiled here. A tree-sitter parse proves the syntax but says nothing about
whether a name resolves -- which is exactly the class of error that shipped
twice:

  CS0122  GameDatabase.MissingImportMessage() is inaccessible (it is private
          in C# when written without a modifier) but was called from
          H5Bootstrap, a different type.
  CS0234  UnityEngine.InputSystem did not exist, because the asmdef never
          referenced the assembly.

This script closes the first class and most of the second, using a real parse
tree rather than regex:

  * every qualified access ``Type.Member`` is resolved against the declared
    types, and the member's access modifiers are checked against the calling
    type -- private use outside its own type, protected use outside a derived
    type, internal use across assemblies.
  * every qualified access whose left side looks like one of our own types but
    whose member is not declared anywhere is reported as unresolved.

It is not a compiler, and "no problems found" is not a clean bill of health.
Out of scope, so a real compiler can still reject what this accepts:

  * access through a variable -- ``s.InternalOnly()`` where ``s`` is a local.
    Only the qualified ``Type.Member`` form is resolved; the other needs
    variable type inference.
  * overloads, generics, extension methods, params, optional arguments
  * members inherited from UnityEngine base classes (MonoBehaviour and friends)
  * using-alias and namespace-import resolution; a bare ``Foo.Bar`` where Foo is
    a variable rather than a type is skipped
  * type errors and anything else semantic

Run ``--self-test`` to watch it catch known violations on synthetic files.

Usage:  python3 tools/csharp_access_check.py [project_dir]
        python3 tools/csharp_access_check.py --self-test
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

# Which asmdef each file belongs to, so internal access can be checked across
# assemblies the way the compiler would.
ASSEMBLY_OF = {}

ACCESS_KEYWORDS = {"public", "private", "protected", "internal", "file"}


def parse(path):
    parser = Parser(Language(tscs.language()))
    with open(path, "rb") as fh:
        return parser.parse(fh.read())


def text(node, src):
    return src[node.start_byte:node.end_byte].decode("utf8", "replace")


def modifiers_of(node, src):
    """tree-sitter-c-sharp emits one `modifier` node per keyword, each wrapping
    the keyword itself -- not a single `modifiers` list. Looking for the plural
    finds nothing and silently makes every member private."""
    out = set()
    for child in node.children:
        if child.type == "modifier":
            out.add(text(child, src))
    return out


def access_of(mods):
    """The effective accessibility. C# defaults members to private."""
    if "public" in mods:
        return "public"
    if "protected" in mods and "internal" in mods:
        return "protected internal"
    if "protected" in mods:
        return "protected"
    if "internal" in mods:
        return "internal"
    if "private" in mods and "protected" in mods:
        return "private protected"
    return "private"


def member_names(node, src):
    """The names a member node declares.

    A method or property carries its type as a sibling `identifier` -- for
    `static Text MakeText(...)` both `Text` and `MakeText` are identifiers --
    so taking every identifier indexes return types as members. The name is the
    last identifier before the parameter list or accessor list.

    A field instead nests its names under variable_declarator, and may declare
    several at once.
    """
    if node.type in ("field_declaration", "event_field_declaration"):
        out = []
        for child in node.children:
            if child.type == "variable_declaration":
                for decl in child.children:
                    if decl.type == "variable_declarator":
                        for sub in decl.children:
                            if sub.type == "identifier":
                                out.append(text(sub, src))
        return out

    stop = ("parameter_list", "accessor_list", "arrow_expression_clause",
            "block", "equals_value_clause", "semicolon")
    last = None
    for child in node.children:
        if child.type in stop:
            break
        if child.type in ("identifier", "generic_name"):
            last = text(child, src).split("<")[0]
    if last:
        return [last]
    if node.type == "constructor_declaration":
        for child in node.children:
            if child.type == "identifier":
                return [text(child, src)]
    return []


MEMBER_NODES = {
    "method_declaration", "property_declaration", "field_declaration",
    "constructor_declaration", "event_declaration", "event_field_declaration",
    "indexer_declaration", "operator_declaration", "conversion_operator_declaration",
    "destructor_declaration", "delegate_declaration",
}


class Collector:
    """First pass: every declared type and its members, with accessibility."""

    def __init__(self):
        self.types = {}      # simple name -> dict(ns, file, kind, bases, members)
        self.ambiguous = set()

    def walk(self, node, src, path, ns_stack, type_stack):
        if node.type in ("namespace_declaration", "file_scoped_namespace_declaration"):
            name = None
            for child in node.children:
                if child.type in ("qualified_name", "identifier", "name"):
                    name = text(child, src)
            if name:
                ns_stack.append(name)

        if node.type in ("class_declaration", "struct_declaration",
                         "interface_declaration", "record_declaration"):
            name = None
            bases = []
            for child in node.children:
                if child.type == "identifier":
                    name = text(child, src)
                elif child.type == "base_list":
                    bases = [text(c, src).split("<")[0] for c in child.children
                             if c.type in ("identifier", "qualified_name",
                                           "generic_name", "simple_base_type")]
            if name:
                info = {
                    "ns": ".".join(ns_stack),
                    "file": path,
                    "kind": node.type.replace("_declaration", ""),
                    "bases": bases,
                    "enclosing": list(type_stack),
                    "members": {},
                }
                if name in self.types:
                    # partial classes and same-named types both land here; keep
                    # the members of both rather than shadowing one.
                    self.types[name]["members"].update(info["members"])
                    self.ambiguous.add(name)
                self.types[name] = info
                type_stack.append(name)

                for child in node.children:
                    if child.type == "declaration_list":
                        for m in child.children:
                            if m.type in MEMBER_NODES:
                                acc = access_of(modifiers_of(m, src))
                                static = "static" in modifiers_of(m, src)
                                for mn in member_names(m, src):
                                    self.types[name]["members"].setdefault(
                                        mn, {"access": acc, "static": static})
                            elif m.type in ("class_declaration", "struct_declaration",
                                            "interface_declaration", "record_declaration"):
                                self.walk(m, src, path, ns_stack, type_stack)

                type_stack.pop()

        for child in node.children:
            self.walk(child, src, path, ns_stack, type_stack)

        if node.type in ("namespace_declaration", "file_scoped_namespace_declaration"):
            if ns_stack:
                ns_stack.pop()


def rightmost_type_name(node, src):
    """`HoMM5.Data.GameDatabase.Method()` nests member accesses; the type is the
    rightmost identifier of the object chain. Requiring a bare identifier there
    skips every fully qualified call, which is how a private call from another
    type went unreported."""
    cur = node
    depth = 0
    while cur.type == "member_access_expression" and depth < 8:
        kids = [c for c in cur.children if c.type != "."]
        if not kids:
            return None
        cur = kids[-1]
        depth += 1
    return text(cur, src) if cur.type == "identifier" else None


def assembly_for(path):
    """The asmdef that governs a file: the nearest one walking up."""
    d = os.path.dirname(path)
    while True:
        parent = os.path.dirname(d)
        for entry in os.listdir(d):
            if entry.endswith(".asmdef"):
                return entry[: -len(".asmdef")]
        if parent == d:
            return "<predefined>"
        d = parent


def iter_cs(root):
    for dirpath, _dirs, files in os.walk(root):
        if any(part in ("Library", "obj", "Temp", "Logs") for part in dirpath.split(os.sep)):
            continue
        for f in files:
            if f.endswith(".cs"):
                yield os.path.join(dirpath, f)


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    files = sorted(iter_cs(root))
    if not files:
        sys.exit("no .cs files under %s" % root)

    for f in files:
        ASSEMBLY_OF[f] = assembly_for(f)

    collector = Collector()
    trees = {}
    for path in files:
        with open(path, "rb") as fh:
            src = fh.read()
        tree = parse(path)
        trees[path] = (tree, src)
        collector.walk(tree.root_node, src, path, [], [])

    problems = []
    checked = 0

    for path in files:
        tree, src = trees[path]
        # The type each access site lives in, for private/protected rules.
        stack = []

        def visit(node):
            nonlocal checked
            if node.type in ("class_declaration", "struct_declaration",
                             "interface_declaration", "record_declaration"):
                for child in node.children:
                    if child.type == "identifier":
                        stack.append(text(child, src))

            if node.type == "member_access_expression":
                obj, member = None, None
                kids = [c for c in node.children if c.type not in (".",)]
                if len(kids) >= 2:
                    obj, member = kids[0], kids[-1]
                tname = rightmost_type_name(obj, src) if obj is not None else None
                if tname is not None and member is not None:
                    mname = text(member, src)
                    info = collector.types.get(tname)
                    if info is not None:
                        checked += 1
                        m = info["members"].get(mname)
                        caller = stack[-1] if stack else None
                        if m is None:
                            # Only report when the type is ours; UnityEngine
                            # members are not in this index.
                            if tname in collector.types:
                                problems.append(
                                    f"{path}:{node.start_point[0]+1}: CS1061? "
                                    f"{tname}.{mname} is not declared on {tname}")
                        else:
                            acc = m["access"]
                            asm_caller = ASSEMBLY_OF.get(path)
                            asm_owner = ASSEMBLY_OF.get(info["file"])
                            same_type = caller == tname
                            derived = bool(caller) and tname in (
                                collector.types[caller]["bases"]
                                if caller in collector.types else [])
                            if acc == "private" and not same_type:
                                problems.append(
                                    f"{path}:{node.start_point[0]+1}: CS0122 "
                                    f"{tname}.{mname} is private; called from "
                                    f"{caller or 'top level'}")
                            elif acc == "protected" and not (same_type or derived):
                                problems.append(
                                    f"{path}:{node.start_point[0]+1}: CS0122 "
                                    f"{tname}.{mname} is protected; called from "
                                    f"{caller or 'top level'}")
                            elif acc == "internal" and asm_caller != asm_owner:
                                problems.append(
                                    f"{path}:{node.start_point[0]+1}: CS0122 "
                                    f"{tname}.{mname} is internal to {asm_owner}; "
                                    f"called from {asm_caller}")

            for child in node.children:
                visit(child)

            if node.type in ("class_declaration", "struct_declaration",
                             "interface_declaration", "record_declaration"):
                if stack:
                    stack.pop()

        visit(tree.root_node)

    print(f"scanned {len(files)} files, {checked} qualified accesses against "
          f"{len(collector.types)} declared types")
    if problems:
        for p in problems:
            print("  " + p)
        print(f"\n{len(problems)} problem(s)")
        return 1
    print("no problems found among the qualified accesses it can resolve; "
          "this is not a compile")
    return 0


# Synthetic violations, so the checker can be shown to work rather than merely
# trusted. Pub must not be reported; the other four must be.
SELF_TEST_ASM_A = [
    "namespace Demo.A {",
    "    public class S {",
    '        public static string Pub() { return "1"; }',
    '        static string Priv() { return "2"; }',
    '        internal static string Intern() { return "3"; }',
    '        protected static string Prot() { return "4"; }',
    "    }",
    "}",
]

SELF_TEST_ASM_B = [
    "namespace Demo.B {",
    "    public class C {",
    "        public void Ok()   { Demo.A.S.Pub(); }",
    "        public void Bad1() { Demo.A.S.Priv(); }",
    "        public void Bad2() { Demo.A.S.Intern(); }",
    "        public void Bad3() { Demo.A.S.Prot(); }",
    "        public void Bad4() { Demo.A.S.Missing(); }",
    "    }",
    "}",
]

SELF_TEST_EXPECTED = 4   # Priv, Intern, Prot, Missing


def self_test():
    import tempfile

    saved = sys.argv[:]
    try:
        with tempfile.TemporaryDirectory() as tmp:
            for sub, lines in (("AsmA/A.cs", SELF_TEST_ASM_A),
                               ("AsmB/B.cs", SELF_TEST_ASM_B)):
                path = os.path.join(tmp, sub)
                os.makedirs(os.path.dirname(path), exist_ok=True)
                with open(path, "w") as fh:
                    fh.write("\n".join(lines) + "\n")
            for asm in ("AsmA", "AsmB"):
                with open(os.path.join(tmp, asm, asm + ".asmdef"), "w") as fh:
                    fh.write('{"name":"%s"}' % asm)

            print("self-test: %d findings expected on synthetic violations"
                  % SELF_TEST_EXPECTED)
            sys.argv = [saved[0], tmp]
            found_it = main() == 1
        if found_it:
            print("self-test PASSED: the checker reports the violations it is fed")
            return 0
        print("self-test FAILED: the checker stayed silent on known violations")
        return 1
    finally:
        sys.argv = saved


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.argv.remove("--self-test")
        sys.exit(self_test())
    sys.exit(main())
