"""Point Unity scene/prefab files at the project's felt/paper primitives instead of the 2D package's
default shapes, by swapping only the GUID text. The internal sprite IDs are the same in both, so
nothing else changes and the diff stays one line per reference. (Saving a scene through Unity
reorders the whole file instead.)

    python tools/art/remap_primitive_guids.py Assets/Scenes/Select.unity [more files...]
Run from the project root.
"""
import glob
import re
import sys

PACKAGE = glob.glob("Library/PackageCache/com.unity.2d.sprite@*/Editor/ObjectMenuCreation/DefaultAssets/Textures/v2/")[0]


def guid(meta):
    return re.search(r"guid: (\w+)", open(meta, encoding="utf-8").read()).group(1)


def main():
    swaps = {guid(PACKAGE + n + ".png.meta"): guid("Assets/Sprites/" + n + ".png.meta")
             for n in ("Circle", "Triangle", "Capsule", "Square")}
    for path in sys.argv[1:]:
        text = open(path, encoding="utf-8", newline="").read()
        count = 0
        for old, new in swaps.items():
            count += text.count("guid: " + old)
            text = text.replace("guid: " + old, "guid: " + new)
        open(path, "w", encoding="utf-8", newline="").write(text)
        print(f"{path}: {count} references")


if __name__ == "__main__":
    main()
