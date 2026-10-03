#!/usr/bin/env python3
"""Validate the translation sources and assemble the Iron Lung RU package.

    python tools/build.py          проверить файлы и собрать build/stage (файлы перевода без BepInEx)
    python tools/build.py --zip    то же + скачать официальные BepInEx и XUnity и собрать dist/IronLung_RUS.zip

Requires only the Python standard library (3.8+).
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import sys
import time
import urllib.request
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_TERMINAL = os.path.join(ROOT, "src", "terminal")
PACKAGE = os.path.join(ROOT, "package")
TEXT_DIR = os.path.join("BepInEx", "Translation", "ru", "Text")
PLUGIN_DIR = os.path.join("BepInEx", "plugins", "IronLungRu")
STAGE = os.path.join(ROOT, "build", "stage")
CACHE = os.path.join(ROOT, "build", "cache")
DIST = os.path.join(ROOT, "dist")
ZIP_NAME = "IronLung_RUS.zip"

# Official third-party releases the package is built on (files are taken unchanged).
UPSTREAM = [
    ("https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x64_5.4.23.2.zip",
     "f752ce4e838f4c305b9da1404b6745f2cff23b8bfd494f79f0c84d0a01f59b46"),
    ("https://github.com/bbepis/XUnity.AutoTranslator/releases/download/v5.4.0/XUnity.AutoTranslator-BepInEx-5.4.0.zip",
     "3ce06b6558b4f9822892011c966215e162e58b79738e520b844b9b87a2ba00fd"),
]
UPSTREAM_SKIP = {"changelog.txt"}

SECTION = re.compile(r"^=== (.+?) ===\s*$")
errors = []
warnings = []


def err(where, msg):
    errors.append(f"{where}: {msg}")


def rel(path):
    return os.path.relpath(path, ROOT).replace("\\", "/")


# ---------------------------------------------------------------- XUnity file format

def xunity_escape(s):
    return s.replace("\\", "\\\\").replace("\r", "\\r").replace("\n", "\\n").replace("=", "\\=")


def unescaped_equals(line):
    """Positions of '=' that XUnity treats as the key/value separator."""
    out, i = [], 0
    while i < len(line):
        c = line[i]
        if c == "\\":
            i += 2
            continue
        if c == "=":
            out.append(i)
        i += 1
    return out


def check_xunity_file(path):
    """XUnity silently drops a line with more than one unescaped '='. Catch that here."""
    with open(path, encoding="utf-8-sig") as f:
        for n, line in enumerate(f, 1):
            line = line.rstrip("\r\n")
            if not line.strip() or line.lstrip().startswith("//"):
                continue
            eq = unescaped_equals(line)
            if len(eq) != 1:
                err(f"{rel(path)}:{n}", "в строке должен быть ровно один знак = между оригиналом и переводом "
                    f"(найдено {len(eq)}). Знак = внутри текста пишите как \\=")
                continue
            key, value = line[:eq[0]], line[eq[0] + 1:]
            if "//" in value:
                err(f"{rel(path)}:{n}", "// внутри перевода XUnity считает началом комментария")
            for tok in ("{{A}}", "{{B}}", "{{C}}", "{{D}}"):
                if tok in key and tok not in value:
                    warnings.append(f"{rel(path)}:{n}: в переводе нет {tok} (игра подставляет туда число) — "
                                    "проверьте, что это сделано намеренно")


def check_subtitles(path):
    """subtitles.txt: [ClipName] headers, then lines «start end text» in seconds."""
    if not os.path.isfile(path):
        return
    clip, last_end = None, 0.0
    with open(path, encoding="utf-8-sig") as f:
        for n, raw in enumerate(f, 1):
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            where = f"{rel(path)}:{n}"
            if line.startswith("[") and line.endswith("]"):
                clip, last_end = line[1:-1], 0.0
                continue
            parts = line.split(" ", 2)
            try:
                start, end = float(parts[0]), float(parts[1])
            except (ValueError, IndexError):
                err(where, "ожидается «начало конец текст», например «1.6 3.4 Начинаем погружение.»")
                continue
            if clip is None:
                err(where, "перед репликами нужна строка с именем клипа, например [RadioChatter]")
            elif len(parts) < 3 or not parts[2].strip():
                err(where, "нет текста реплики")
            elif end <= start:
                err(where, "конец реплики должен быть позже начала")
            elif start < last_end:
                err(where, "реплика начинается раньше, чем закончилась предыдущая")
            last_end = max(last_end, end)


# ---------------------------------------------------------------- terminal sources

def read_sections(path):
    sections, current = {}, None
    with open(path, encoding="utf-8-sig") as f:
        for raw in f:
            line = raw.rstrip("\r\n")
            m = SECTION.match(line)
            if m:
                name = m.group(1).split(" (")[0].strip().upper()
                current = sections.setdefault(name, [])
                continue
            if current is None:
                continue  # preamble with # comments
            current.append(line)
    return {k: "\n".join(v).strip("\n") for k, v in sections.items()}


def norm_query(s):
    s = re.sub(r"<[^>]+>", "", s).lower()
    s = "".join(ch for ch in s if ch.isalnum() or ch.isspace())
    return re.sub(r"\s+", " ", s).strip()


def build_terminal():
    with open(os.path.join(ROOT, "tools", "terminal_en.json"), encoding="utf-8") as f:
        en = json.load(f)
    files = sorted(fn for fn in os.listdir(SRC_TERMINAL) if re.match(r"^\d\d_.*\.txt$", fn))
    header_file = os.path.join(SRC_TERMINAL, "00_header.txt")
    entry_files = [os.path.join(SRC_TERMINAL, fn) for fn in files if not fn.startswith("00_")]
    if len(entry_files) != len(en["entries"]):
        err(rel(SRC_TERMINAL), f"ожидается {len(en['entries'])} файлов статей, найдено {len(entry_files)}")
        return None, None

    header_ru = read_sections(header_file).get("ТЕКСТ", "")
    if not header_ru:
        err(rel(header_file), "нет раздела === ТЕКСТ ===")

    responses = {}
    resp_path = os.path.join(SRC_TERMINAL, "responses.txt")
    with open(resp_path, encoding="utf-8-sig") as f:
        for n, line in enumerate(f, 1):
            line = line.rstrip("\r\n")
            if not line.strip() or line.startswith("#"):
                continue
            if " => " not in line:
                err(f"{rel(resp_path)}:{n}", "ожидается «английский => русский»")
                continue
            k, v = line.split(" => ", 1)
            responses[k] = v.strip()
    for k in en["responses"]:
        if k not in responses:
            err(rel(resp_path), f"нет перевода для «{k}»")

    texts, aliases, owner = [], [], {}
    for i, path in enumerate(entry_files):
        sec = read_sections(path)
        text = sec.get("ТЕКСТ", "")
        if not text:
            err(rel(path), "нет раздела === ТЕКСТ ===")
        texts.append(text)
        al = [a.strip() for a in sec.get("ЗАПРОСЫ", "").split("\n") if a.strip()]
        for a in al:
            if a.lower() != norm_query(a):
                err(rel(path), f"запрос «{a}» нельзя набрать в игре: только строчные буквы, цифры и одиночные пробелы")
            for v in {a.lower(), a.lower().replace("ё", "е")}:
                if v in owner and owner[v] != i:
                    err(rel(path), f"запрос «{v}» уже используется в {os.path.basename(entry_files[owner[v]])}")
                owner[v] = i
        aliases.append(al)
    for i, e in enumerate(en["entries"]):
        for q in e["queries"]:
            owner.setdefault(q, i)
    for i, text in enumerate(texts):
        for link in re.findall(r"<color=red>(.*?)</color>", text):
            n = norm_query(link).replace("ё", "е")
            if n not in owner:
                err(rel(entry_files[i]), f"красную ссылку «{link}» нельзя ввести как запрос — "
                    f"добавьте «{n}» в раздел ЗАПРОСЫ нужной статьи")
    for p in [header_file, resp_path] + entry_files:
        with open(p, encoding="utf-8-sig") as f:
            if "//" in f.read().split("=== ТЕКСТ ===")[-1]:
                err(rel(p), "// в тексте XUnity считает началом комментария")

    out = [
        "// ФАЙЛ СОБИРАЕТСЯ АВТОМАТИЧЕСКИ из src/terminal скриптом tools/build.py. Не редактируйте его вручную.",
        "// Игра дописывает каждый ответ терминала к уже выведенному тексту, поэтому текст разбивается",
        "// на части регулярками (sr:) и каждая часть переводится отдельно.",
        "",
        r'sr:"^(<c\.o\.i informational terminal>\nThis is a local database\.  Be sure to update before each descent\.\nLast updated today \[5/378\])\n([\s\S]+)$"=$1\n$2',
        r'sr:"^([\s\S]+?)\n\n\n([\s\S]+)$"=$1\n\n\n$2',
        "",
        xunity_escape(en["header"]) + "=" + xunity_escape(header_ru),
        "",
    ]
    for k in en["responses"]:
        out.append(xunity_escape(k) + "=" + xunity_escape(responses.get(k, k)))
    out.append("")
    for i, e in enumerate(en["entries"]):
        out.append("// " + e["text"].split("\n")[1])
        out.append(xunity_escape(e["text"]) + "=" + xunity_escape(texts[i]))
    terminal_txt = "\n".join(out) + "\n"

    q = ["# ФАЙЛ СОБИРАЕТСЯ АВТОМАТИЧЕСКИ из src/terminal скриптом tools/build.py.",
         "# Формат: английский_запрос=вариант1;вариант2;..."]
    for i, e in enumerate(en["entries"]):
        q.append(e["queries"][0] + "=" + ";".join(aliases[i]))
    queries_txt = "\n".join(q) + "\n"
    return terminal_txt, queries_txt


# ---------------------------------------------------------------- packaging

def write_text(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8-sig", newline="\r\n") as f:
        f.write(text)


def stage(terminal_txt, queries_txt, version):
    if os.path.isdir(STAGE):
        shutil.rmtree(STAGE)
    shutil.copytree(PACKAGE, STAGE)
    write_text(os.path.join(STAGE, TEXT_DIR, "RuTerminal.txt"), terminal_txt)
    write_text(os.path.join(STAGE, PLUGIN_DIR, "queries.txt"), queries_txt)
    shutil.copy2(os.path.join(ROOT, "plugin", "IronLungRu.cs"), os.path.join(STAGE, PLUGIN_DIR, "IronLungRu.cs"))
    readme = os.path.join(STAGE, "README_RUS.txt")
    with open(readme, encoding="utf-8-sig") as f:
        text = f.read().replace("{VERSION}", version)
    write_text(readme, text)


def download(url, sha):
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, url.rsplit("/", 1)[1])
    for attempt in range(6):
        if os.path.isfile(path) and hashlib.sha256(open(path, "rb").read()).hexdigest() == sha:
            return path
        try:
            print("  скачиваю", url)
            urllib.request.urlretrieve(url, path)
        except Exception as e:  # GitHub sometimes answers 503
            print("  ошибка:", e)
            time.sleep(3 * (attempt + 1))
    raise SystemExit(f"не удалось скачать {url} (или не совпала SHA-256)")


def make_zip():
    os.makedirs(DIST, exist_ok=True)
    out = os.path.join(DIST, ZIP_NAME)
    names = set()
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        def add(name, data):
            names.add(name)
            z.writestr(name, data)
        for url, sha in UPSTREAM:
            with zipfile.ZipFile(download(url, sha)) as up:
                for info in up.infolist():
                    if info.is_dir() or info.filename in UPSTREAM_SKIP or info.filename in names:
                        continue
                    add(info.filename, up.read(info))
        for dp, _, fns in os.walk(STAGE):
            for fn in fns:
                full = os.path.join(dp, fn)
                name = os.path.relpath(full, STAGE).replace("\\", "/")
                if name in names:
                    raise SystemExit(f"{name} уже есть в BepInEx/XUnity — конфликт файлов")
                with open(full, "rb") as f:
                    add(name, f.read())
    print(f"готово: {rel(out)} ({os.path.getsize(out) // 1024} КБ, {len(names)} файлов)")


def main():
    if not sys.stdout.isatty():
        sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--zip", action="store_true", help="собрать dist/IronLung_RUS.zip")
    args = ap.parse_args()
    with open(os.path.join(ROOT, "VERSION"), encoding="utf-8") as f:
        version = f.read().strip()

    for fn in sorted(os.listdir(os.path.join(PACKAGE, TEXT_DIR))):
        if fn.endswith(".txt") and not fn.startswith("_") and fn != "resizer.txt":
            check_xunity_file(os.path.join(PACKAGE, TEXT_DIR, fn))
    check_subtitles(os.path.join(PACKAGE, PLUGIN_DIR, "subtitles.txt"))
    terminal_txt, queries_txt = build_terminal()
    for w in warnings:
        print("предупреждение:", w)
    if errors:
        print("Найдены ошибки:\n  " + "\n  ".join(errors))
        sys.exit(1)
    stage(terminal_txt, queries_txt, version)
    print(f"проверка пройдена, перевод собран в {rel(STAGE)} (версия {version})")
    if args.zip:
        make_zip()


if __name__ == "__main__":
    main()
