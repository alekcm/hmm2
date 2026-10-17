#!/usr/bin/env python3
"""Flag type names a C# file uses without a namespace or using that brings them in.

The other checkers do not resolve types. A file in ``namespace HoMM5.Map`` that
writes ``MapObjectDef`` without ``using HoMM5.Data;`` (or ``HoMM5.Data.``) fails
with CS0246, and nothing else in this directory would see it.

For each project type this works out its namespace (from the declaring file)
and the names visible from a use site: the file's own namespace and every
enclosing one, plus each ``using`` directive's namespace. A capitalised
identifier in type or expression position that names a project type is flagged
when that type's namespace is not visible and the file does not declare it.
Qualified names (``HoMM5.Data.X``) and member names (``a.X``) are skipped.

Well-known .NET and Unity types are checked the same way against a table of
the namespaces that define them.

A heuristic, not a compiler: it does not model nested types, aliases or
``using static``. Read every hit.

Usage: python3 tools/csharp_type_scope_check.py [root]   (default: UnityProject/Assets/Scripts)
"""
import pathlib
import sys

from tree_sitter import Language, Parser
import tree_sitter_c_sharp as tcs

PARSER = Parser(Language(tcs.language()))

TYPE_DECLS = {
    "class_declaration", "struct_declaration", "enum_declaration",
    "interface_declaration", "record_declaration", "delegate_declaration",
}

# Namespace of well-known external types. A name is only flagged when it is
# used, is not a project type, and its namespace is not imported by the file.
EXTERNAL = {
    "List": "System.Collections.Generic", "Dictionary": "System.Collections.Generic",
    "HashSet": "System.Collections.Generic", "Queue": "System.Collections.Generic",
    "Stack": "System.Collections.Generic", "KeyValuePair": "System.Collections.Generic",
    "IEnumerable": "System.Collections.Generic", "IReadOnlyList": "System.Collections.Generic",
    "IReadOnlyDictionary": "System.Collections.Generic", "SortedDictionary": "System.Collections.Generic",
    "StringComparer": "System", "StringComparison": "System", "Action": "System",
    "Func": "System", "Math": "System", "Exception": "System", "Enum": "System",
    "Array": "System", "Convert": "System", "TimeSpan": "System", "Guid": "System",
    "Environment": "System", "NotImplementedException": "System",
    "InvalidOperationException": "System", "ArgumentException": "System",
    "StringBuilder": "System.Text", "Encoding": "System.Text",
    "File": "System.IO", "Path": "System.IO", "Directory": "System.IO",
    "StreamReader": "System.IO", "StreamWriter": "System.IO",
    "MonoBehaviour": "UnityEngine", "ScriptableObject": "UnityEngine",
    "GameObject": "UnityEngine", "Transform": "UnityEngine", "Component": "UnityEngine",
    "Mathf": "UnityEngine", "Debug": "UnityEngine", "Vector2": "UnityEngine",
    "Vector3": "UnityEngine", "Vector4": "UnityEngine", "Quaternion": "UnityEngine",
    "Color": "UnityEngine", "Color32": "UnityEngine", "Rect": "UnityEngine",
    "Bounds": "UnityEngine", "Ray": "UnityEngine", "Texture2D": "UnityEngine",
    "Texture": "UnityEngine", "RenderTexture": "UnityEngine", "Material": "UnityEngine",
    "Shader": "UnityEngine", "Mesh": "UnityEngine", "Renderer": "UnityEngine",
    "Camera": "UnityEngine", "Light": "UnityEngine", "Sprite": "UnityEngine",
    "Resources": "UnityEngine", "Coroutine": "UnityEngine", "WaitForSeconds": "UnityEngine",
    "AudioSource": "UnityEngine", "AudioClip": "UnityEngine", "Application": "UnityEngine",
    "Time": "UnityEngine", "Input": "UnityEngine", "Screen": "UnityEngine",
    "Gizmos": "UnityEngine", "AnimationCurve": "UnityEngine", "Keyframe": "UnityEngine",
    "Canvas": "UnityEngine", "RectTransform": "UnityEngine", "Graphics": "UnityEngine",
    "Plane": "UnityEngine", "SerializeField": "UnityEngine", "Header": "UnityEngine",
    "Tooltip": "UnityEngine", "RuntimeInitializeOnLoadMethodAttribute": "UnityEngine",
    "Text": "UnityEngine.UI", "Button": "UnityEngine.UI", "Image": "UnityEngine.UI",
    "Slider": "UnityEngine.UI", "Toggle": "UnityEngine.UI", "InputField": "UnityEngine.UI",
    "ScrollRect": "UnityEngine.UI", "Outline": "UnityEngine.UI", "Shadow": "UnityEngine.UI",
    "LayoutElement": "UnityEngine.UI", "RawImage": "UnityEngine.UI", "Mask": "UnityEngine.UI",
    "Graphic": "UnityEngine.UI", "Selectable": "UnityEngine.UI",
    "EventSystem": "UnityEngine.EventSystems", "PointerEventData": "UnityEngine.EventSystems",
    "SceneManager": "UnityEngine.SceneManagement",
    "JsonConvert": "Newtonsoft.Json", "JsonUtility": "UnityEngine",
}


def text(src, n):
    return src[n.start_byte:n.end_byte].decode("utf8", "replace")


def walk(n):
    stack = [n]
    while stack:
        x = stack.pop()
        yield x
        stack.extend(reversed(x.children))


def namespace_of(node, src):
    """Dotted namespace a declaration sits in ('' for the global namespace)."""
    parts = []
    p = node.parent
    while p is not None:
        if p.type in ("namespace_declaration", "file_scoped_namespace_declaration"):
            name = p.child_by_field_name("name")
            if name is not None:
                parts.append(text(src, name))
        p = p.parent
    return ".".join(reversed(parts))


def file_namespace(root, src):
    for n in root.children:
        if n.type == "file_scoped_namespace_declaration":
            name = n.child_by_field_name("name")
            return text(src, name) if name is not None else ""
    for n in walk(root):
        if n.type == "namespace_declaration":
            name = n.child_by_field_name("name")
            return text(src, name) if name is not None else ""
    return ""


def usings_of(root, src):
    out = set()
    for n in walk(root):
        if n.type != "using_directive":
            continue
        # `using X;`, not `using static X;` or `using A = B;`
        if any(c.type == "=" for c in n.children):
            continue
        if any(c.type == "static" for c in n.children):
            continue
        for c in n.children:
            if c.type in ("identifier", "qualified_name"):
                out.add(text(src, c))
    return out


def declared_types(root, src):
    """Project type name -> set of namespaces that declare it."""
    found = {}
    for n in walk(root):
        if n.type in TYPE_DECLS:
            name = n.child_by_field_name("name")
            if name is None:
                continue
            ns = namespace_of(n, src)
            found.setdefault(text(src, name), set()).add(ns)
    return found


def visible_namespaces(ns, usings):
    seen = set()
    parts = ns.split(".") if ns else []
    for i in range(len(parts), -1, -1):
        seen.add(".".join(parts[:i]))
    seen |= usings
    return seen


def is_skipped_position(node):
    """Identifiers that are not a type reference in this file."""
    p = node.parent
    if p is None:
        return True
    if p.type in TYPE_DECLS or p.type in ("namespace_declaration", "using_directive",
                                          "file_scoped_namespace_declaration",
                                          "qualified_name", "type_parameter"):
        return True
    if p.type == "assignment_expression":
        # `Action = ...` in an object initializer names a member, not a type.
        return p.child_by_field_name("left") == node
    if p.type == "member_access_expression":
        # Only the receiver (`Type.Member`) can be a type; the name cannot.
        return p.child_by_field_name("name") == node
    if p.type == "member_access":
        return True
    if p.type == "generic_name" and p.parent is not None and p.parent.type == "qualified_name":
        # `System.Action<T>`: the name is already qualified.
        return True
    if p.type in ("parameter", "variable_declarator", "property_declaration",
                  "method_declaration", "constructor_declaration", "field_declaration_list",
                  "enum_member_declaration", "attribute_argument", "named_argument"):
        return p.child_by_field_name("name") == node
    return False


def member_names(root, src):
    """Names this file declares as members (properties, fields, methods, events).

    A simple name that is also a member of the file binds to that member first,
    so `Resources` inside a class that has a `Resources` property is not a type.
    """
    out = set()
    for n in walk(root):
        if n.type in ("property_declaration", "method_declaration", "event_declaration",
                      "enum_member_declaration", "event_field_declaration"):
            name = n.child_by_field_name("name")
            if name is not None:
                out.add(text(src, name))
        elif n.type == "variable_declarator":
            name = n.child_by_field_name("name")
            if name is not None:
                out.add(text(src, name))
    return out


def check_file(path, parser_src, project_types, report):
    src = path.read_bytes()
    root = PARSER.parse(src).root_node
    ns = file_namespace(root, src)
    usings = usings_of(root, src)
    visible = visible_namespaces(ns, usings)
    local_types = declared_types(root, src)
    members = member_names(root, src)
    hits = []
    for n in walk(root):
        if n.type != "identifier":
            continue
        name = text(src, n)
        if name in local_types or name in members:
            continue
        if name in project_types:
            if is_skipped_position(n):
                continue
            ok = any(decl_ns in visible or decl_ns == "" for decl_ns in project_types[name])
            if not ok:
                hits.append((n.start_point[0] + 1, name, sorted(project_types[name])))
        elif name in EXTERNAL:
            if is_skipped_position(n):
                continue
            if EXTERNAL[name] not in visible:
                hits.append((n.start_point[0] + 1, name, [EXTERNAL[name]]))
    for line, name, where in hits:
        report.append((path, line, name, where, ns, sorted(usings)))


def main(argv):
    root = pathlib.Path(argv[1] if len(argv) > 1 else "UnityProject/Assets/Scripts")
    files = sorted(root.rglob("*.cs"))
    parsed = {}
    project_types = {}
    for f in files:
        src = f.read_bytes()
        tree = PARSER.parse(src).root_node
        parsed[f] = None
        for name, nss in declared_types(tree, src).items():
            project_types.setdefault(name, set()).update(nss)
    report = []
    for f in files:
        check_file(f, None, project_types, report)
    for path, line, name, where, ns, usings in report:
        rel = path.as_posix()
        print(f"{rel}:{line}: '{name}' is in {', '.join(w or 'global' for w in where)}; "
              f"file namespace '{ns or 'global'}', usings {', '.join(usings) or 'none'}")
    print(f"scanned {len(files)} files, {sum(len(v) for v in project_types.values())} type declarations; "
          f"{len(report)} type reference(s) outside visible namespaces")
    return 1 if report else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
