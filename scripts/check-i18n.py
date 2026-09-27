#!/usr/bin/env python3
"""检查多语言文本是否完整。

- 代码 / XAML 中用到的键在每种语言里都存在
- 各语言的键与默认语言（zh-CN）一致
- 每条翻译的占位符（{0}、{1} …）与默认语言一致

用法: python3 scripts/check-i18n.py [--unused]
      --unused  额外列出没有被代码引用的键
发现问题时退出码为 1。
"""
import json
import pathlib
import re
import sys
from collections import defaultdict

REPO = pathlib.Path(__file__).resolve().parent.parent
PROJECTS = ("Kairo.Core", "Kairo", "Kairo.Cli")
DEFAULT_LANGUAGE = "zh-CN"
PLURAL_FORMS = ("one", "other")

# L.T("key") / L.Plural("key", n) / L.List("key") / new LocalizedOption("key") / LocExtension.Create("key")，
# 整个参数列表都会被扫描，三元表达式里的键也能找到
CALL = re.compile(r"(?:\bL\.(?:T|Plural|List)|\bLocalizedOption|\bLocExtension\.Create)\s*\(")
XAML = re.compile(r"\{l:Loc\s+([^}\s]+)\s*\}")
KEY_LITERAL = re.compile(r'"([A-Za-z][\w-]*(?:\.[\w-]+)+\.?)"')
PLACEHOLDER = re.compile(r"\{(\d+)(?:[,:][^}]*)?\}")


def call_arguments(text, open_index):
    """返回从 open_index 处的左括号开始、到匹配的右括号为止的参数文本（跳过字符串里的括号）"""
    depth, i, in_string = 0, open_index, False
    while i < len(text):
        char = text[i]
        if in_string:
            if char == "\\":
                i += 1
            elif char == '"':
                in_string = False
        elif char == '"':
            in_string = True
        elif char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0:
                return text[open_index + 1:i]
        i += 1
    return text[open_index + 1:]


def flatten(value, prefix, out):
    if isinstance(value, dict):
        for key, child in value.items():
            flatten(child, f"{prefix}.{key}" if prefix else key, out)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            flatten(child, f"{prefix}.{index}", out)
    else:
        out[prefix] = value


def load_catalogs():
    catalogs = defaultdict(dict)
    for project in PROJECTS:
        for file in sorted((REPO / project / "Localization" / "Languages").glob("*.json")):
            flatten(json.loads(file.read_text(encoding="utf-8")), "", catalogs[file.stem])
    return catalogs


def used_keys():
    used = defaultdict(set)
    for file in list(REPO.rglob("*.cs")) + list(REPO.rglob("*.axaml")):
        parts = file.relative_to(REPO).parts
        if {"bin", "obj", "Legacy"} & set(parts):
            continue
        lines = file.read_text(encoding="utf-8", errors="ignore").splitlines()
        text = "\n".join(line for line in lines if not line.lstrip().startswith("//"))
        for match in CALL.finditer(text):
            for key in KEY_LITERAL.findall(call_arguments(text, match.end() - 1)):
                used[key].add(str(file.relative_to(REPO)))
        for match in XAML.finditer(text):
            used[match.group(1)].add(str(file.relative_to(REPO)))
    return used


def resolves(catalog, key):
    """键可以是普通文本、带 one/other 的复数形式、列表（key.0 …）或以点结尾的动态前缀"""
    if key.endswith("."):
        return any(k.startswith(key) for k in catalog)
    return key in catalog or any(k.startswith(key + ".") for k in catalog)


def base_of(key):
    head, _, tail = key.rpartition(".")
    return head if tail in PLURAL_FORMS else key


def main():
    catalogs = load_catalogs()
    if DEFAULT_LANGUAGE not in catalogs:
        print(f"找不到默认语言 {DEFAULT_LANGUAGE} 的语言文件")
        return 1
    default = catalogs[DEFAULT_LANGUAGE]
    problems = []

    used = used_keys()
    for key, files in sorted(used.items()):
        for language, catalog in sorted(catalogs.items()):
            if not resolves(catalog, key):
                problems.append(f"缺少键 [{language}] {key}  ← {', '.join(sorted(files))}")

    for language, catalog in sorted(catalogs.items()):
        if language == DEFAULT_LANGUAGE:
            continue
        for key in sorted(default):
            if key not in catalog and not any(k.startswith(key + ".") for k in catalog):
                problems.append(f"未翻译 [{language}] {key}")
        for key, text in sorted(catalog.items()):
            source_key = base_of(key)
            if source_key not in default and not (key in default):
                problems.append(f"多余的键 [{language}] {key}")
                continue
            source = default.get(key, default.get(source_key, ""))
            translated, original = set(PLACEHOLDER.findall(str(text))), set(PLACEHOLDER.findall(str(source)))
            # 单数形式可以省略数量（如 "Started the tunnel"），但不能出现原文没有的占位符
            if translated != original and not (key.endswith(".one") and translated <= original):
                problems.append(f"占位符不一致 [{language}] {key}: {source!r} → {text!r}")

    for problem in problems:
        print(problem)

    if "--unused" in sys.argv:
        for key in sorted(default):
            if key in used or any(key.startswith(u) for u in used if u.endswith(".")):
                continue
            if any(key.startswith(u + ".") for u in used):
                continue
            print(f"未使用 {key}")

    counts = ", ".join(f"{language} {len(catalog)}" for language, catalog in sorted(catalogs.items()))
    print(f"语言文件: {counts}；代码中引用 {len(used)} 个键；问题 {len(problems)} 个")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
