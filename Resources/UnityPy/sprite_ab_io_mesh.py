import sys, os, argparse, glob, math, struct, re, json, tempfile, shutil
from collections import defaultdict
from typing import Any, Dict, List, Set, Tuple
import UnityPy
from PIL import Image
import random, traceback

def ensure_dir(p): os.makedirs(p, exist_ok=True)
def sanitize_name(s: str) -> str: return (s or "").replace("\\", "_").replace("/", "_").strip()
def round_int(x: float) -> int: return int(round(x))

def bytes_from_maybe_array(x) -> bytes:
    if x is None: return b""
    if isinstance(x, (bytes, bytearray, memoryview)): return bytes(x)
    if hasattr(x, "data"):
        try: return bytes(x.data)
        except: pass
    if isinstance(x, (list, tuple)):
        try: return bytes(x)
        except: pass
    try: return bytes(x)
    except: return b""

def to_float(v, default=0.0):
    try: return float(v)
    except: return float(default)

def to_int(v, default=0):
    try: return int(v)
    except: return int(default)

def get_loop_flag(clip) -> bool:
    settings = getattr(clip, "m_AnimationClipSettings", None)
    if settings is not None:
        for name in ("m_LoopTime", "m_LoopBlend"):
            try:
                value = getattr(settings, name)
                return bool(value)
            except Exception:
                pass
    return True

def get_clip_length(clip) -> float:
    try:
        muscle = getattr(clip, "m_MuscleClip", None)
        if muscle is not None:
            stop_time = getattr(muscle, "m_StopTime", None)
            if stop_time is not None:
                return to_float(stop_time, 0.0)
    except Exception:
        pass
    return 0.0

def get_sprite_rect(spr):
    tr = None
    try: tr = spr.m_RD.textureRect
    except Exception: tr = None
    if tr is None:
        try: tr = spr.m_Rect
        except Exception: tr = None
    return tr

def get_texture_pid(spr) -> int:
    try:
        tex_ptr = spr.m_RD.texture
        return int(getattr(tex_ptr, "path_id", 0) or 0)
    except Exception:
        return 0

def get_pixels_per_unit(spr) -> float:
    for name in ("m_PixelsToUnits", "pixelsToUnits"):
        try:
            value = getattr(spr, name)
            if value is not None:
                return to_float(value, 100.0)
        except Exception:
            pass
    return 100.0

def get_border(spr):
    border = None
    for name in ("m_Border", "border"):
        try:
            border = getattr(spr, name)
            if border is not None:
                break
        except Exception:
            pass
    if border is None:
        return [0.0, 0.0, 0.0, 0.0]

    result = []
    for name in ("x", "y", "z", "w"):
        try: result.append(to_float(getattr(border, name), 0.0))
        except Exception: result.append(0.0)
    while len(result) < 4:
        result.append(0.0)
    return result[:4]

def get_pivot01(spr, rect_w: int, rect_h: int):
    pivot = None
    for name in ("m_Pivot", "pivot"):
        try:
            pivot = getattr(spr, name)
            if pivot is not None:
                break
        except Exception:
            pass
    if pivot is None or rect_w <= 0 or rect_h <= 0:
        return [0.5, 0.5]
    try:
        px = to_float(getattr(pivot, "x", 0.5 * rect_w), 0.5 * rect_w)
        py = to_float(getattr(pivot, "y", 0.5 * rect_h), 0.5 * rect_h)
        return [px / rect_w, py / rect_h]
    except Exception:
        return [0.5, 0.5]

def clip_name_key(clip_name: str, path_id: int) -> str:
    return sanitize_name(clip_name or f"clip_{path_id}")

def build_sprite_entry(pid: int, spr, file_name: str, default_duration: float):
    rect = get_sprite_rect(spr)
    rect_w = max(1, round_int(getattr(rect, "width", 0) if rect is not None else 0))
    rect_h = max(1, round_int(getattr(rect, "height", 0) if rect is not None else 0))
    return {
        "origName": sanitize_name(getattr(spr, "m_Name", None) or f"sprite_{pid}"),
        "origPathID": pid,
        "duration": default_duration,
        "pivot01": get_pivot01(spr, rect_w, rect_h),
        "border": get_border(spr),
        "rectPx": [rect_w, rect_h],
        "file": file_name,
    }

def dedupe_file_name(base_name: str, used_names: Dict[str, int]) -> str:
    stem = sanitize_name(base_name) or "sprite"
    count = used_names.get(stem, 0) + 1
    used_names[stem] = count
    return f"{stem}.png" if count == 1 else f"{stem}__{count}.png"

def clone_json_value(value):
    return json.loads(json.dumps(value))

def natural_suffix_sort_key(file_name: str):
    stem = os.path.splitext(os.path.basename(file_name))[0]
    prefix, number, _ = parse_trailing_number(stem)
    if number is None:
        return (sanitize_name(stem).lower(), 1, 0, file_name.lower())
    return (sanitize_name(prefix).lower(), 0, number, file_name.lower())

def list_clip_png_files(clip_dir: str) -> List[str]:
    files = []
    for entry in os.listdir(clip_dir):
        path = os.path.join(clip_dir, entry)
        if os.path.isfile(path) and entry.lower().endswith(".png"):
            files.append(entry)
    files.sort(key=natural_suffix_sort_key)
    return files

def normalize_vec2_list(value, default=None):
    default = default or [0.5, 0.5]
    if isinstance(value, list) and len(value) >= 2:
        return [to_float(value[0], default[0]), to_float(value[1], default[1])]
    return list(default)

def normalize_vec4_list(value, default=None):
    default = default or [0.0, 0.0, 0.0, 0.0]
    if isinstance(value, list) and len(value) >= 4:
        return [
            to_float(value[0], default[0]),
            to_float(value[1], default[1]),
            to_float(value[2], default[2]),
            to_float(value[3], default[3]),
        ]
    return list(default)

def parse_trailing_number(name: str):
    match = re.match(r"^(.*?)(\d+)$", sanitize_name(name or ""))
    if not match:
        return None, None, None
    prefix, digits = match.groups()
    return prefix, int(digits), len(digits)

def infer_frame_name_pattern(frames: List[dict], clip_name: str):
    next_index = 0
    width = 3
    prefix = f"{sanitize_name(clip_name)}_"
    for frame in frames:
        stem = os.path.splitext(frame.get("file") or "")[0]
        parsed_prefix, parsed_index, parsed_width = parse_trailing_number(stem)
        if parsed_index is not None:
            prefix = parsed_prefix
            width = max(width, parsed_width or 0)
            next_index = max(next_index, parsed_index + 1)
            continue

        parsed_prefix, parsed_index, parsed_width = parse_trailing_number(frame.get("origName") or "")
        if parsed_index is not None:
            prefix = parsed_prefix
            width = max(width, parsed_width or 0)
            next_index = max(next_index, parsed_index + 1)

    return prefix, width, next_index

def generate_random_meta_path_id(used_ids: Set[int]) -> int:
    while True:
        value = random.randint(-INT_64, INT_64)
        if value != 0 and value not in used_ids:
            used_ids.add(value)
            return value

def derive_frame_duration(meta: dict, frames: List[dict], keyframes: List[dict]) -> float:
    for frame in frames:
        duration = to_float(frame.get("duration"), 0.0)
        if duration > 0:
            return duration

    key_times = [to_float(entry.get("time"), -1.0) for entry in keyframes]
    for idx in range(len(key_times) - 1):
        delta = key_times[idx + 1] - key_times[idx]
        if delta > 0:
            return delta

    frame_count = max(0, to_int(meta.get("frameCount"), len(keyframes) or len(frames)))
    clip_length = to_float(meta.get("length"), 0.0)
    if frame_count > 0 and clip_length > 0:
        return clip_length / float(frame_count)

    sample_rate = to_float(meta.get("sampleRate"), 0.0)
    if sample_rate > 0:
        return 1.0 / sample_rate

    return 0.0

def sync_clip_meta_with_pngs(clip_dir: str, meta: dict) -> Tuple[dict, bool]:
    png_files = list_clip_png_files(clip_dir)
    if not png_files:
        return meta, False

    frames = [clone_json_value(frame) for frame in (meta.get("frames") or [])]
    keyframes = [clone_json_value(entry) for entry in (meta.get("keyframes") or [])]
    clip_name = meta.get("clipName") or os.path.basename(clip_dir)

    ordered_files = []
    used_files = set()
    for frame in frames:
        file_name = frame.get("file") or ""
        if file_name in png_files and file_name not in used_files:
            ordered_files.append(file_name)
            used_files.add(file_name)
    for file_name in png_files:
        if file_name not in used_files:
            ordered_files.append(file_name)
            used_files.add(file_name)

    frame_duration = derive_frame_duration(meta, frames, keyframes)
    default_frame = clone_json_value(frames[-1] if frames else {})
    default_pivot01 = normalize_vec2_list(default_frame.get("pivot01"), [0.5, 0.5])
    default_border = normalize_vec4_list(default_frame.get("border"), [0.0, 0.0, 0.0, 0.0])
    default_rect = default_frame.get("rectPx") if isinstance(default_frame.get("rectPx"), list) and len(default_frame.get("rectPx")) >= 2 else [1, 1]
    prefix, width, next_index = infer_frame_name_pattern(frames, clip_name)

    frame_by_file = {frame.get("file") or "": frame for frame in frames if frame.get("file")}
    used_ids = {to_int(frame.get("origPathID"), 0) for frame in frames if to_int(frame.get("origPathID"), 0) != 0}
    for entry in keyframes:
        path_id = to_int(entry.get("pathId"), 0)
        if path_id != 0:
            used_ids.add(path_id)

    updated_frames = []
    updated_keyframes = []

    for idx, file_name in enumerate(ordered_files):
        existing = frame_by_file.get(file_name)
        if existing is not None:
            frame = clone_json_value(existing)
            frame["duration"] = frame_duration
            frame["pivot01"] = normalize_vec2_list(frame.get("pivot01"), default_pivot01)
            frame["border"] = normalize_vec4_list(frame.get("border"), default_border)
            if not isinstance(frame.get("rectPx"), list) or len(frame.get("rectPx")) < 2:
                frame["rectPx"] = list(default_rect)
            frame["file"] = file_name
        else:
            name_number = next_index
            next_index += 1
            orig_name = f"{prefix}{name_number:0{width}d}"
            path_id = generate_random_meta_path_id(used_ids)
            frame = {
                "origName": orig_name,
                "origPathID": path_id,
                "duration": frame_duration,
                "pivot01": list(default_pivot01),
                "border": list(default_border),
                "rectPx": list(default_rect),
                "file": file_name,
            }

        updated_frames.append(frame)
        updated_keyframes.append({
            "index": idx,
            "time": frame_duration * idx,
            "pathId": to_int(frame.get("origPathID"), 0),
            "spriteName": frame.get("origName") or f"sprite_{idx}",
            "file": file_name,
        })

    updated_meta = clone_json_value(meta)
    updated_meta["frames"] = updated_frames
    updated_meta["keyframes"] = updated_keyframes
    updated_meta["frameCount"] = len(updated_keyframes)
    updated_meta["spritePathIds"] = [to_int(entry.get("pathId"), 0) for entry in updated_keyframes]
    updated_meta["length"] = frame_duration * len(updated_keyframes)

    changed = json.dumps(updated_meta, ensure_ascii=False, sort_keys=True) != json.dumps(meta, ensure_ascii=False, sort_keys=True)
    return updated_meta, changed

def gather_clip_data(env) -> Dict[str, dict]:
    sprites_by_pid = {}
    for obj in env.objects:
        if obj.type.name != "Sprite":
            continue
        try: sprites_by_pid[obj.path_id] = obj.read()
        except Exception: pass

    clips = {}
    for obj in env.objects:
        if obj.type.name != "AnimationClip":
            continue
        try:
            clip = obj.read()
        except Exception:
            continue

        clip_name = getattr(clip, "m_Name", None) or f"clip_{obj.path_id}"
        clip_key = clip_name_key(clip_name, obj.path_id)
        sample_rate = to_float(getattr(clip, "m_SampleRate", 0.0), 0.0)
        length = get_clip_length(clip)

        keyframes = []
        pptr_curves = getattr(clip, "m_PPtrCurves", None) or getattr(clip, "pptrCurves", None)
        if pptr_curves:
            for c in pptr_curves:
                attr = getattr(c, "attribute", "") or getattr(c, "m_Attribute", "")
                if attr not in ("m_Sprite", "sprite", "Sprite"):
                    continue
                for idx, kf in enumerate(getattr(c, "curve", []) or []):
                    sp_ptr = getattr(kf, "value", None)
                    pid = int(getattr(sp_ptr, "path_id", 0) or 0) if sp_ptr else 0
                    if pid and pid in sprites_by_pid:
                        keyframes.append({
                            "index": len(keyframes),
                            "time": to_float(getattr(kf, "time", idx / sample_rate if sample_rate > 0 else idx), idx),
                            "pathId": pid,
                        })

        if not keyframes:
            mapping = getattr(clip, "m_ClipBindingConstant", None)
            pmap = getattr(mapping, "pptrCurveMapping", None) if mapping else None
            if pmap:
                for idx, pp in enumerate(pmap):
                    pid = int(getattr(pp, "path_id", 0) or 0)
                    if pid and pid in sprites_by_pid:
                        keyframes.append({
                            "index": len(keyframes),
                            "time": idx / sample_rate if sample_rate > 0 else idx,
                            "pathId": pid,
                        })

        if not keyframes:
            obj_curves = getattr(clip, "m_ObjectCurves", None)
            if obj_curves:
                for c in obj_curves:
                    for idx, kf in enumerate(getattr(c, "curve", []) or []):
                        sp_ptr = getattr(kf, "value", None)
                        pid = int(getattr(sp_ptr, "path_id", 0) or 0) if sp_ptr else 0
                        if pid and pid in sprites_by_pid:
                            keyframes.append({
                                "index": len(keyframes),
                                "time": to_float(getattr(kf, "time", idx / sample_rate if sample_rate > 0 else idx), idx),
                                "pathId": pid,
                            })

        if not keyframes:
            continue

        ordered_pids = [entry["pathId"] for entry in keyframes]
        first_sprite = sprites_by_pid.get(ordered_pids[0]) if ordered_pids else None
        texture_pid = get_texture_pid(first_sprite) if first_sprite else 0
        default_duration = (1.0 / sample_rate) if sample_rate > 0 else 0.0

        unique_pids = []
        seen_pids = set()
        for pid in ordered_pids:
            if pid in seen_pids:
                continue
            seen_pids.add(pid)
            unique_pids.append(pid)

        frame_entries = []
        used_file_names = {}
        for pid in unique_pids:
            spr = sprites_by_pid.get(pid)
            sp_name = sanitize_name(getattr(spr, "m_Name", None) or f"sprite_{pid}") if spr else f"sprite_{pid}"
            file_name = dedupe_file_name(sp_name, used_file_names)
            frame_entries.append(build_sprite_entry(pid, spr, file_name, default_duration))

        file_by_pid = {frame["origPathID"]: frame["file"] for frame in frame_entries}
        name_by_pid = {frame["origPathID"]: frame["origName"] for frame in frame_entries}

        for entry in keyframes:
            pid = entry["pathId"]
            entry["spriteName"] = name_by_pid.get(pid, f"sprite_{pid}")
            entry["file"] = file_by_pid.get(pid, "")

        clips[clip_key] = {
            "clipName": clip_name,
            "clipPathId": obj.path_id,
            "unityVersion": "",
            "length": length,
            "loop": get_loop_flag(clip),
            "originalSampleRate": sample_rate,
            "sampleRate": sample_rate,
            "frameCount": len(keyframes),
            "pixelsPerUnit": get_pixels_per_unit(first_sprite) if first_sprite else 100.0,
            "texturePathId": texture_pid,
            "spritePathIds": ordered_pids,
            "frames": frame_entries,
            "keyframes": keyframes,
        }

    return clips

INT_64 = 2 ** 63 - 1
_ASSETS_TOOLS = None
_ASSETS_CLASSDB_LOADED = False


class SpriteAnimation:
    def __init__(self, animation_clip):
        self.anim_name = animation_clip.peek_name() or ""
        self.animation_clip = animation_clip
        self._animation_clip_data = None
        self.mono_behaviour = None
        self.sprites = []
        self.texture = None
        self._sprite_name_format = None

    @property
    def animation_clip_data(self):
        if self._animation_clip_data is None:
            self._animation_clip_data = self.animation_clip.parse_as_dict()
        return self._animation_clip_data

    @property
    def sprite_name_format(self):
        if self._sprite_name_format:
            return self._sprite_name_format
        if not self.sprites:
            self._sprite_name_format = f"{sanitize_name(self.anim_name)}_{{}}"
            return self._sprite_name_format
        sprite_name, _, _ = self.sprites[0]
        sep_at = sprite_name.rfind("_")
        if sep_at == -1:
            self._sprite_name_format = f"{sanitize_name(self.anim_name)}_{{}}"
        else:
            width = len(sprite_name[sep_at + 1:])
            self._sprite_name_format = f"{sprite_name[:sep_at + 1]}{{:0{width}d}}"
        return self._sprite_name_format


def ensure_assets_tools():
    global _ASSETS_TOOLS, _ASSETS_CLASSDB_LOADED
    if _ASSETS_TOOLS is not None:
        return _ASSETS_TOOLS

    try:
        import clr  # type: ignore[import-not-found]
    except Exception as ex:
        raise RuntimeError("import-clips 需要 pythonnet(clr) 支持，请改用 Python 3.12/3.11 并安装 pythonnet") from ex

    libs_dir = os.path.join(os.path.dirname(__file__), "libs")
    dll_path = os.path.join(libs_dir, "AssetsTools.NET.dll")
    classdb_path = os.path.join(libs_dir, "classdata.tpk")
    if not os.path.isfile(dll_path):
        raise RuntimeError(f"未找到 AssetsTools.NET.dll: {dll_path}")
    if not os.path.isfile(classdb_path):
        raise RuntimeError(f"未找到 classdata.tpk: {classdb_path}")

    clr.AddReference(dll_path)
    from AssetsTools.NET import AssetFileInfo, AssetTypeArrayInfo, AssetsFileWriter  # type: ignore[import-not-found]
    from AssetsTools.NET.Extra import AssetClassID, AssetsManager  # type: ignore[import-not-found]

    _ASSETS_TOOLS = {
        "manager": AssetsManager(),
        "AssetClassID": AssetClassID,
        "AssetFileInfo": AssetFileInfo,
        "AssetTypeArrayInfo": AssetTypeArrayInfo,
        "AssetsFileWriter": AssetsFileWriter,
        "classdb": classdb_path,
    }
    return _ASSETS_TOOLS


def ensure_classdb_loaded(manager, classdb_path: str):
    global _ASSETS_CLASSDB_LOADED
    if _ASSETS_CLASSDB_LOADED:
        return
    manager.LoadClassPackage(classdb_path)
    _ASSETS_CLASSDB_LOADED = True


def float_to_u32(value: float) -> int:
    return struct.unpack(">I", struct.pack(">f", float(value)))[0]


def load_animations(env) -> Dict[str, SpriteAnimation]:
    animations: Dict[str, SpriteAnimation] = {}
    mono_behaviours = []
    objects = {}
    for obj in env.objects:
        objects[obj.path_id] = obj
        if obj.type.name == "AnimationClip":
            animation = SpriteAnimation(obj)
            animations[(animation.anim_name or "").lower()] = animation
        elif obj.type.name == "MonoBehaviour":
            mono_behaviours.append(obj)

    sprites_of_animations = {}
    for animation in animations.values():
        anim_data = animation.animation_clip.parse_as_dict()
        anim_pptr_mapping = anim_data.get("m_ClipBindingConstant", {}).get("pptrCurveMapping", [])
        for entry in anim_pptr_mapping:
            obj = objects.get(entry.get("m_PathID"))
            if not obj:
                continue
            sprites_of_animations[obj.path_id] = animation

            obj_name = obj.peek_name() or f"sprite_{obj.path_id}"
            idx = -1
            sep_at = obj_name.rfind("_")
            if sep_at != -1:
                try:
                    idx = int(obj_name[sep_at + 1:])
                except Exception:
                    idx = -1

            animation.sprites.append((obj_name, obj, idx))
            if animation.texture is None:
                try:
                    texture_pid = obj.parse_as_dict()["m_RD"]["texture"]["m_PathID"]
                    texture = objects.get(texture_pid)
                    if texture:
                        animation.texture = texture
                except Exception:
                    pass

    for mono_behaviour in mono_behaviours:
        try:
            mono_data = mono_behaviour.parse_as_dict()
        except Exception:
            continue
        sprites = mono_data.get("sprites")
        if not isinstance(sprites, list):
            continue
        for sprite in sprites:
            sprite_obj = objects.get(sprite.get("m_PathID"))
            if not sprite_obj:
                continue
            animation = sprites_of_animations.get(sprite_obj.path_id)
            if animation:
                animation.mono_behaviour = mono_behaviour
                break

    return animations


def collect_track_bindings(env):
    anim_id_to_playable_asset_id = {}
    playable_asset_id_to_track_clip = {}
    tracks = []

    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            mono_data = obj.parse_as_dict()
        except Exception:
            continue
        if "m_Clips" not in mono_data and "m_Clip" not in mono_data:
            continue
        if "m_Clips" in mono_data:
            for clip in mono_data["m_Clips"]:
                asset = clip.get("m_Asset") or {}
                playable_asset_id_to_track_clip[asset.get("m_PathID")] = clip
            tracks.append((obj, mono_data))
        else:
            clip = mono_data.get("m_Clip") or {}
            anim_id_to_playable_asset_id[clip.get("m_PathID")] = obj.path_id

    return anim_id_to_playable_asset_id, playable_asset_id_to_track_clip, tracks


def update_sprites(bundle_path: str, update_list: List[dict], temp_file: str):
    assets = ensure_assets_tools()
    manager = assets["manager"]
    AssetClassID = assets["AssetClassID"]
    AssetFileInfo = assets["AssetFileInfo"]
    AssetTypeArrayInfo = assets["AssetTypeArrayInfo"]
    AssetsFileWriter = assets["AssetsFileWriter"]

    manager.UnloadAll()
    ensure_classdb_loaded(manager, assets["classdb"])

    bundle = manager.LoadBundleFile(os.path.abspath(bundle_path))
    bundle_file_inst = manager.LoadAssetsFileFromBundle(bundle, 0)
    bundle_file = bundle_file_inst.file

    bundle_data_info = bundle_file.GetAssetsOfType(AssetClassID.AssetBundle)[0]
    bundle_data = manager.GetBaseField(bundle_file_inst, bundle_data_info)
    rand = random.Random()

    def add_sprites(needly: int, last_sprite_id: int, id_offset=0, name_format="new_sprite_{}"):
        sprite_id_added = []

        last_sprite_info = bundle_file.GetAssetInfo(last_sprite_id)
        last_sprite_field = manager.GetBaseField(bundle_file_inst, last_sprite_info)

        for i in range(needly):
            path_id = None
            while not path_id or bundle_file.GetAssetInfo(path_id) is not None:
                path_id = rand.randint(-INT_64, INT_64)
            last_sprite_field.Get("m_Name").AsString = name_format.format(i + id_offset)
            new_info = AssetFileInfo.Create(bundle_file, path_id, int(AssetClassID.Sprite))
            new_info.SetNewData(last_sprite_field)
            bundle_file.Metadata.AddAssetInfo(new_info)
            sprite_id_added.append(path_id)

        preload_insert_at = []
        preload_insert_entry = None
        m_preload_table = bundle_data.Get("m_PreloadTable.Array")
        for i in range(m_preload_table.AsArray.size):
            entry = m_preload_table.Get(i)
            path_id = entry.Get("m_PathID").AsLong
            if path_id != last_sprite_id:
                continue
            if preload_insert_entry is None:
                preload_insert_entry = entry
            preload_insert_at.append(i)
        if preload_insert_entry is None:
            return []

        for insert_at in reversed(preload_insert_at):
            for i, path_id in enumerate(sprite_id_added):
                clone = preload_insert_entry.Clone()
                clone.Get("m_PathID").AsLong = path_id
                m_preload_table.Children.Insert(insert_at + i + 1, clone)
        m_preload_table.AsArray = AssetTypeArrayInfo(m_preload_table.AsArray.size + needly * len(preload_insert_at))

        m_container = bundle_data.Get("m_Container.Array")
        last_sprite_index = -1
        last_sprite_entry = None
        second_updated = {}

        for i in range(m_container.AsArray.size):
            entry = m_container.Get(i)
            path_id = entry.Get("second.asset.m_PathID").AsLong
            if path_id == last_sprite_id:
                last_sprite_entry = entry
                last_sprite_index = i

            preload_index = entry.Get("second.preloadIndex").AsInt
            preload_size = entry.Get("second.preloadSize").AsInt
            data_updated = second_updated.get(preload_index)
            if data_updated is None:
                offset = 0
                size = 0
                for inserted in preload_insert_at:
                    if inserted < preload_index:
                        offset += len(sprite_id_added)
                    if preload_index <= inserted < preload_index + preload_size:
                        size += len(sprite_id_added)
                data_updated = {
                    "preloadIndex": preload_index + offset,
                    "preloadSize": preload_size + size,
                }
                second_updated[preload_index] = data_updated

            entry.Get("second.preloadIndex").AsInt = data_updated["preloadIndex"]
            entry.Get("second.preloadSize").AsInt = data_updated["preloadSize"]

        if last_sprite_entry is None:
            return []

        for i, path_id in enumerate(sprite_id_added):
            clone = last_sprite_entry.Clone()
            clone.Get("second.asset.m_PathID").AsLong = path_id
            m_container.Children.Insert(last_sprite_index + i + 1, clone)
        m_container.AsArray = AssetTypeArrayInfo(m_container.AsArray.size + len(sprite_id_added))
        return sprite_id_added

    def remove_sprites(needly: int, sprites: List[Tuple[str, Any, int]]):
        sprite_id_removed = []
        for i in range(needly):
            _, sprite, _ = sprites[-(i + 1)]
            bundle_file.Metadata.RemoveAssetInfo(bundle_file.Metadata.GetAssetInfo(sprite.path_id))
            sprite_id_removed.append(sprite.path_id)

        m_preload_table = bundle_data.Get("m_PreloadTable.Array")
        preload_removed = []
        for i in range(m_preload_table.AsArray.size):
            entry = m_preload_table.Get(i)
            if entry.Get("m_PathID").AsLong in sprite_id_removed:
                preload_removed.append(i)
        for i in reversed(preload_removed):
            m_preload_table.Children.RemoveAt(i)
        m_preload_table.AsArray = AssetTypeArrayInfo(m_preload_table.AsArray.size - len(preload_removed))

        m_container = bundle_data.Get("m_Container.Array")
        container_removed = []
        second_updated = {}
        for i in range(m_container.AsArray.size):
            entry = m_container.Get(i)
            path_id = entry.Get("second.asset.m_PathID").AsLong
            if path_id in sprite_id_removed:
                container_removed.append(i)
                continue

            preload_index = entry.Get("second.preloadIndex").AsInt
            preload_size = entry.Get("second.preloadSize").AsInt
            data_updated = second_updated.get(preload_index)
            if data_updated is None:
                offset = 0
                size = 0
                for removed in preload_removed:
                    if removed < preload_index:
                        offset -= 1
                    if preload_index <= removed < preload_index + preload_size:
                        size -= 1
                data_updated = {
                    "preloadIndex": preload_index + offset,
                    "preloadSize": preload_size + size,
                }
                second_updated[preload_index] = data_updated

            entry.Get("second.preloadIndex").AsInt = data_updated["preloadIndex"]
            entry.Get("second.preloadSize").AsInt = data_updated["preloadSize"]

        for i in reversed(container_removed):
            m_container.Children.RemoveAt(i)
        m_container.AsArray = AssetTypeArrayInfo(m_container.AsArray.size - len(container_removed))
        return sprite_id_removed

    changed = []
    for update in update_list:
        anim = update["anim"]
        expected = update["expected"]
        current = len(anim.sprites)
        delta = expected - current
        if delta == 0:
            continue
        if delta > 0:
            changed.append((anim.anim_name.lower(), True, add_sprites(delta, anim.sprites[-1][1].path_id, anim.sprites[-1][2] + 1, anim.sprite_name_format)))
        else:
            changed.append((anim.anim_name.lower(), False, remove_sprites(-delta, anim.sprites)))

    bundle_data_info.SetNewData(bundle_data)
    bundle.file.BlockAndDirInfo.DirectoryInfos[0].SetNewData(bundle_file)
    writer = AssetsFileWriter(os.path.abspath(temp_file))
    bundle.file.Write(writer)
    writer.Close()
    manager.UnloadAll()
    return changed


def build_pointer_array(existing: List[dict], path_ids: List[int]) -> List[dict]:
    template = dict(existing[-1]) if existing else {"m_FileID": 0, "m_PathID": 0}
    result = []
    for path_id in path_ids:
        clone = dict(template)
        clone["m_PathID"] = int(path_id)
        result.append(clone)
    return result


def ensure_streamed_clip_data(anim_data: dict, keyframes: List[dict], sample_rate: float):
    target_count = len(keyframes)
    data = [0] * (target_count * 7 + 2)
    for index in range(max(0, target_count - 1)):
        next_time = to_float(keyframes[index + 1].get("time"), (index + 1) / sample_rate if sample_rate > 0 else index + 1)
        data[2 + index * 7 + 4] = float_to_u32(index)
        data[2 + index * 7 + 5] = float_to_u32(next_time)
        data[2 + index * 7 + 6] = 1
    if len(data) >= 2:
        data[-2] = float_to_u32(math.inf)
        data[-1] = 0
    anim_data["m_MuscleClip"]["m_Clip"]["data"]["m_StreamedClip"]["data"] = data


def normalize_rect(rect) -> Tuple[int, int, int, int]:
    if isinstance(rect, dict):
        return (
            round_int(rect.get("x", 0)),
            round_int(rect.get("y", 0)),
            max(1, round_int(rect.get("width", 1))),
            max(1, round_int(rect.get("height", 1))),
        )
    return (
        round_int(getattr(rect, "x", 0)),
        round_int(getattr(rect, "y", 0)),
        max(1, round_int(getattr(rect, "width", 1))),
        max(1, round_int(getattr(rect, "height", 1))),
    )


def composite_image_to_rect(tex_img: Image.Image, rect, repl_img: Image.Image):
    x, y, w, h = normalize_rect(rect)
    left = max(0, min(x, tex_img.width - w))
    top = max(0, min(tex_img.height - (y + h), tex_img.height - h))
    tex_img.paste((0, 0, 0, 0), (left, top, left + w, top + h))
    if repl_img.size != (w, h):
        repl_img = repl_img.resize((w, h), resample=Image.LANCZOS)
    tex_img.alpha_composite(repl_img.convert("RGBA"), (left, top))


def remap_sprite_uvs(sprite_data: dict, old_texture_size: Tuple[int, int], new_texture_size: Tuple[int, int], old_rect, new_rect):
    vertex_data = sprite_data.get("m_RD", {}).get("m_VertexData", {})
    raw = bytearray(bytes_from_maybe_array(vertex_data.get("m_DataSize")))
    vertex_count = to_int(vertex_data.get("m_VertexCount"), 0)
    if vertex_count <= 0:
        return

    pos_bytes = vertex_count * 3 * 4
    uv_bytes = vertex_count * 2 * 4
    if len(raw) < pos_bytes + uv_bytes:
        return

    old_w, old_h = old_texture_size
    new_w, new_h = new_texture_size
    old_x, old_y, old_rw, old_rh = normalize_rect(old_rect)
    new_x, new_y, new_rw, new_rh = normalize_rect(new_rect)
    uv_offset = pos_bytes

    for i in range(vertex_count):
        offset = uv_offset + i * 8
        old_u, old_v = struct.unpack_from("<ff", raw, offset)
        px_ratio = 0.0 if old_rw <= 0 else ((old_u * old_w) - old_x) / float(old_rw)
        py_ratio = 0.0 if old_rh <= 0 else ((old_v * old_h) - old_y) / float(old_rh)
        new_u = (new_x + px_ratio * new_rw) / float(new_w)
        new_v = (new_y + py_ratio * new_rh) / float(new_h)
        struct.pack_into("<ff", raw, offset, float(new_u), float(new_v))

    vertex_data["m_DataSize"] = bytes(raw)

def pack_frames_shelf(frame_sizes: List[Tuple[int, int]], padding: int, target_width: int):
    placements = []
    x = padding
    y = padding
    row_height = 0
    used_width = padding

    for frame_w, frame_h in frame_sizes:
        if x > padding and x + frame_w + padding > target_width:
            y += row_height + padding
            x = padding
            row_height = 0
        placements.append((x, y, frame_w, frame_h))
        x += frame_w + padding
        row_height = max(row_height, frame_h)
        used_width = max(used_width, x)

    total_height = y + row_height + padding
    return placements, max(used_width, padding * 2), total_height

def choose_grid_layout(frame_sizes: List[Tuple[int, int]], padding: int, min_width: int = 0, base_height: int = 0):
    if not frame_sizes:
        return padding * 2, padding * 2, []

    ordered_sizes = sorted(frame_sizes, key=lambda item: (item[1], item[0]), reverse=True)
    max_frame_w = max(width for width, _ in frame_sizes)
    min_target_width = max(min_width, max_frame_w + padding * 2)
    area_sum = sum(width * height for width, height in frame_sizes)
    approx_square = int(math.sqrt(max(1, area_sum))) + padding * 2
    total_width = padding
    for frame_w, _ in ordered_sizes:
        total_width += frame_w + padding

    candidate_widths = {min_target_width, max(min_target_width, approx_square)}
    running_width = padding
    for frame_w, _ in ordered_sizes:
        running_width += frame_w + padding
        candidate_widths.add(max(min_target_width, running_width))

    probe_start = min_target_width
    probe_end = max(probe_start, total_width)
    probe_count = min(48, max(12, len(frame_sizes) * 2))
    if probe_end > probe_start:
        for i in range(probe_count + 1):
            ratio = i / float(probe_count)
            width = int(round(probe_start + (probe_end - probe_start) * ratio))
            candidate_widths.add(max(min_target_width, width))

    square_band = max(max_frame_w, approx_square // 3)
    for delta in range(-square_band, square_band + 1, max(8, square_band // 8 or 1)):
        candidate_widths.add(max(min_target_width, approx_square + delta))

    best = None
    for target_width in sorted(candidate_widths):
        placements, used_width, grid_h = pack_frames_shelf(ordered_sizes, padding, target_width)
        total_w = max(min_width, used_width)
        total_h = base_height + grid_h
        long_edge = max(total_w, total_h)
        short_edge = max(1, min(total_w, total_h))
        aspect_ratio = long_edge / float(short_edge)
        area = total_w * total_h
        used_area = sum(width * height for width, height in ordered_sizes)
        waste = area - used_area
        score = (
            round(aspect_ratio, 4),
            area,
            waste,
            total_h,
            total_w,
        )
        if best is None or score < best[0]:
            best = (score, total_w, grid_h, placements)

    assert best is not None
    _, atlas_width, grid_h, placements = best
    return atlas_width, grid_h, placements

def build_rect_sprite_geometry(sprite_data: dict, texture_size: Tuple[int, int], rect, pixels_per_unit: float, pivot01):
    tex_w, tex_h = texture_size
    rect_x, rect_y, rect_w, rect_h = normalize_rect(rect)
    pivot01 = pivot01 if isinstance(pivot01, list) and len(pivot01) >= 2 else [0.5, 0.5]
    pivot_x = to_float(pivot01[0], 0.5) * rect_w
    pivot_y = to_float(pivot01[1], 0.5) * rect_h
    ppu = max(1e-6, to_float(pixels_per_unit, 100.0))

    left = -pivot_x / ppu
    right = (rect_w - pivot_x) / ppu
    bottom = -pivot_y / ppu
    top = (rect_h - pivot_y) / ppu

    positions = [
        (left, bottom, 0.0),
        (right, bottom, 0.0),
        (left, top, 0.0),
        (right, top, 0.0),
    ]
    u0 = rect_x / float(tex_w)
    u1 = (rect_x + rect_w) / float(tex_w)
    v0 = rect_y / float(tex_h)
    v1 = (rect_y + rect_h) / float(tex_h)
    uvs = [
        (u0, v0),
        (u1, v0),
        (u0, v1),
        (u1, v1),
    ]

    raw = bytearray()
    for x, y, z in positions:
        raw.extend(struct.pack("<fff", float(x), float(y), float(z)))
    for u, v in uvs:
        raw.extend(struct.pack("<ff", float(u), float(v)))

    rd = sprite_data["m_RD"]
    vertex_data = rd["m_VertexData"]
    vertex_data["m_VertexCount"] = 4
    vertex_data["m_DataSize"] = bytes(raw)
    rd["m_IndexBuffer"] = struct.pack("<6H", 0, 1, 2, 2, 1, 3)
    if rd.get("m_SubMeshes"):
        rd["m_SubMeshes"][0]["firstByte"] = 0
        rd["m_SubMeshes"][0]["indexCount"] = 6
        rd["m_SubMeshes"][0]["topology"] = 0
        rd["m_SubMeshes"][0]["baseVertex"] = 0
        rd["m_SubMeshes"][0]["firstVertex"] = 0
        rd["m_SubMeshes"][0]["vertexCount"] = 4
        rd["m_SubMeshes"][0]["localAABB"] = {
            "m_Center": {"x": float((left + right) * 0.5), "y": float((bottom + top) * 0.5), "z": 0.0},
            "m_Extent": {"x": float((right - left) * 0.5), "y": float((top - bottom) * 0.5), "z": 0.0},
        }
    rd["textureRect"] = {"x": float(rect_x), "y": float(rect_y), "width": float(rect_w), "height": float(rect_h)}
    if "textureRectOffset" in rd:
        rd["textureRectOffset"] = {"x": 0.0, "y": 0.0}
    if "atlasRectOffset" in rd:
        rd["atlasRectOffset"] = {"x": 0.0, "y": 0.0}
    sprite_data["m_IsPolygon"] = False
    sprite_data["m_Rect"] = {"x": float(-pivot_x), "y": float(-pivot_y), "width": float(rect_w), "height": float(rect_h)}
    sprite_data["m_Offset"] = {
        "x": float((to_float(pivot01[0], 0.5) - 0.5) * rect_w),
        "y": float((to_float(pivot01[1], 0.5) - 0.5) * rect_h),
    }

def rebuild_sprite_uvs_from_local(sprite_data: dict, new_texture_size: Tuple[int, int], new_rect, pixels_per_unit: float, pivot01):
    vertex_data = sprite_data.get("m_RD", {}).get("m_VertexData", {})
    raw = bytearray(bytes_from_maybe_array(vertex_data.get("m_DataSize")))
    vertex_count = to_int(vertex_data.get("m_VertexCount"), 0)
    if vertex_count <= 0:
        return

    pos_bytes = vertex_count * 3 * 4
    uv_bytes = vertex_count * 2 * 4
    if len(raw) < pos_bytes + uv_bytes:
        return

    tex_w, tex_h = new_texture_size
    rect_x, rect_y, rect_w, rect_h = normalize_rect(new_rect)
    pivot_x = to_float(pivot01[0] if isinstance(pivot01, list) and len(pivot01) >= 1 else 0.5, 0.5) * rect_w
    pivot_y = to_float(pivot01[1] if isinstance(pivot01, list) and len(pivot01) >= 2 else 0.5, 0.5) * rect_h
    ppu = max(1e-6, to_float(pixels_per_unit, 100.0))
    uv_offset = pos_bytes

    for i in range(vertex_count):
        pos_offset = i * 12
        local_x, local_y, _ = struct.unpack_from("<fff", raw, pos_offset)
        atlas_px_x = rect_x + local_x * ppu + pivot_x
        atlas_px_y = rect_y + local_y * ppu + pivot_y
        new_u = atlas_px_x / float(tex_w)
        new_v = atlas_px_y / float(tex_h)
        struct.pack_into("<ff", raw, uv_offset + i * 8, float(new_u), float(new_v))

    vertex_data["m_DataSize"] = bytes(raw)

def clear_sprite_uv_region(tex_img: Image.Image, sprite_obj, texture_size: Tuple[int, int], top_offset: int = 0):
    try:
        spr = sprite_obj.read()
    except Exception:
        return
    _, uvs, _ = get_sprite_mesh(spr)
    if not uvs:
        return
    tex_w, tex_h = texture_size
    min_u = max(0.0, min(u for u, _ in uvs))
    max_u = min(1.0, max(u for u, _ in uvs))
    min_v = max(0.0, min(v for _, v in uvs))
    max_v = min(1.0, max(v for _, v in uvs))
    left = max(0, int(math.floor(min_u * tex_w)))
    right = min(tex_img.width, int(math.ceil(max_u * tex_w)))
    top = max(0, top_offset + tex_h - int(math.ceil(max_v * tex_h)))
    bottom = min(tex_img.height, top_offset + tex_h - int(math.floor(min_v * tex_h)))
    if left < right and top < bottom:
        tex_img.paste((0, 0, 0, 0), (left, top, right, bottom))


def scan_bundle(bundle_path: str):
    env = UnityPy.load(bundle_path)
    counts = defaultdict(int)
    for obj in env.objects:
        counts[obj.type.name] += 1
    print("[SCAN] objects by type:")
    for k in sorted(counts): print(f"  {k:16s} : {counts[k]}")
    print(f"[SCAN] total objects: {sum(counts.values())}")

def gather_clip_sprites(env) -> Dict[str, Set[int]]:
    sprite_pids = {obj.path_id for obj in env.objects if obj.type.name == "Sprite"}
    clip_to_pids: Dict[str, Set[int]] = defaultdict(set)

    for obj in env.objects:
        if obj.type.name != "AnimationClip":
            continue
        try:
            clip = obj.read()
        except Exception:
            continue
        clip_name = sanitize_name(clip.m_Name or f"clip_{obj.path_id}")

        found = False
        pptr_curves = getattr(clip, "m_PPtrCurves", None) or getattr(clip, "pptrCurves", None)
        if pptr_curves:
            for c in pptr_curves:
                attr = getattr(c, "attribute", "") or getattr(c, "m_Attribute", "")
                if attr not in ("m_Sprite", "sprite", "Sprite"):
                    continue
                for kf in getattr(c, "curve", []):
                    sp_ptr = getattr(kf, "value", None)
                    pid = getattr(sp_ptr, "path_id", 0) if sp_ptr else 0
                    if pid in sprite_pids:
                        clip_to_pids[clip_name].add(pid); found = True

        if not found:
            mapping = getattr(clip, "m_ClipBindingConstant", None)
            if mapping:
                pmap = getattr(mapping, "pptrCurveMapping", None)
                if pmap:
                    for pp in pmap:
                        pid = getattr(pp, "path_id", 0)
                        if pid in sprite_pids:
                            clip_to_pids[clip_name].add(pid)
                    if clip_to_pids.get(clip_name):
                        found = True

        if not found:
            obj_curves = getattr(clip, "m_ObjectCurves", None)
            if obj_curves:
                for c in obj_curves:
                    for kf in getattr(c, "curve", []):
                        sp_ptr = getattr(kf, "value", None)
                        pid = getattr(sp_ptr, "path_id", 0) if sp_ptr else 0
                        if pid in sprite_pids:
                            clip_to_pids[clip_name].add(pid)

    return clip_to_pids

def export_sprites(bundle_path: str, out_dir: str, all_sprites: bool, unique_names: bool, group_by_texture: bool):
    env = UnityPy.load(bundle_path)

    sprites_by_pid = {}
    textures_by_pid = {}
    clip_data = gather_clip_data(env)
    for obj in env.objects:
        if obj.type.name == "Sprite":
            try: sprites_by_pid[obj.path_id] = obj.read()
            except: pass
        elif obj.type.name == "Texture2D":
            try: textures_by_pid[obj.path_id] = obj.read()
            except: pass

    total_export = 0

    if not all_sprites and clip_data:
        for clip_dir_name, meta in clip_data.items():
            pid_set = meta["spritePathIds"]
            clip_dir = os.path.join(out_dir, clip_dir_name); ensure_dir(clip_dir)
            file_by_pid = {frame["origPathID"]: frame["file"] for frame in meta.get("frames", [])}
            exported = set()
            for pid in pid_set:
                if pid in exported:
                    continue
                exported.add(pid)
                spr = sprites_by_pid.get(pid)
                if not spr: continue
                img = spr.image
                fname = file_by_pid.get(pid)
                if not fname:
                    sp_name = sanitize_name(spr.m_Name or f"sprite_{pid}")
                    fname = f"{sp_name}.png"
                img.save(os.path.join(clip_dir, fname)); total_export += 1
            with open(os.path.join(clip_dir, "clip.json"), "w", encoding="utf-8") as f:
                json.dump(meta, f, ensure_ascii=False, indent=2)
        print(f"[EXPORT] {bundle_path} → {len(clip_data)} 个动画，共导出 {total_export} 张 sprite 到 {out_dir}")
        return

    groups = defaultdict(list)
    for pid, spr in sprites_by_pid.items():
        key = "ALL"
        if group_by_texture:
            try:
                tex_ptr = spr.m_RD.texture
                tex = textures_by_pid.get(tex_ptr.path_id) if tex_ptr else None
                key = sanitize_name(tex.m_Name) if tex and tex.m_Name else "ALL"
            except:
                key = "ALL"
        groups[key].append((pid, spr))

    for key, items in groups.items():
        base = os.path.join(out_dir, "ALL", key) if group_by_texture else os.path.join(out_dir, "ALL")
        ensure_dir(base)
        for pid, spr in items:
            try:
                img = spr.image
                sp_name = sanitize_name(spr.m_Name or f"sprite_{pid}")
                fname = f"{sp_name}__pid{pid}.png" if unique_names else f"{sp_name}.png"
                img.save(os.path.join(base, fname)); total_export += 1
            except Exception as e:
                print(f"[WARN] 导出 {spr.m_Name} 失败：{e}")
    print(f"[EXPORT-ALL] {bundle_path} → 导出全部 Sprite 共 {total_export} 张 到 {out_dir}")

def get_sprite_mesh(spr) -> Tuple[List[Tuple[float,float,float]], List[Tuple[float,float]], List[int]]:
    vd = spr.m_RD.m_VertexData
    vcount = getattr(vd, "m_VertexCount", 0)
    raw = None
    for attr in ("_typelessdata", "m_DataSize", "m_Data"):
        if hasattr(vd, attr):
            raw = bytes_from_maybe_array(getattr(vd, attr))
            if raw: break
    if not raw or vcount <= 0: return [], [], []
    pos_bytes = vcount * 3 * 4
    uv_bytes = vcount * 2 * 4
    uv_first = False
    if len(raw) < pos_bytes + uv_bytes:
        if len(raw) >= uv_bytes + pos_bytes: uv_first = True
        else: return [], [], []
    floats = lambda b: struct.unpack("<" + "f" * (len(b) // 4), b)
    if not uv_first:
        pos_f = floats(raw[:pos_bytes]); uv_f = floats(raw[pos_bytes:pos_bytes + uv_bytes])
    else:
        uv_f = floats(raw[:uv_bytes]); pos_f = floats(raw[uv_bytes:uv_bytes + pos_bytes])
    positions = [(pos_f[i*3+0], pos_f[i*3+1], pos_f[i*3+2]) for i in range(vcount)]
    uvs = [(uv_f[i*2+0], uv_f[i*2+1]) for i in range(vcount)]
    ib_raw = bytes_from_maybe_array(spr.m_RD.m_IndexBuffer)
    idx = [int.from_bytes(ib_raw[i:i+2], "little") for i in range(0, len(ib_raw), 2)]
    return positions, uvs, idx

def sample_bilinear(img: Image.Image, x: float, y: float):
    w, h = img.size
    if w == 0 or h == 0: return (0,0,0,0)
    x = max(0.0, min(x, w - 1.001)); y = max(0.0, min(y, h - 1.001))
    x0 = int(math.floor(x)); x1 = min(x0 + 1, w - 1)
    y0 = int(math.floor(y)); y1 = min(y0 + 1, h - 1)
    fx = x - x0; fy = y - y0
    c00 = img.getpixel((x0, y0)); c10 = img.getpixel((x1, y0))
    c01 = img.getpixel((x0, y1)); c11 = img.getpixel((x1, y1))
    def lerp(a,b,t): return a + (b - a) * t
    rgba = [int(round(lerp(lerp(c00[ch], c10[ch], fx), lerp(c01[ch], c11[ch], fx), fy))) for ch in range(4)]
    return tuple(rgba)

def alpha_over(dst, src):
    sr, sg, sb, sa = src; dr, dg, db, da = dst
    if sa == 0: return dst
    inv = 255 - sa
    r = (sr * 255 + dr * inv) // 255
    g = (sg * 255 + dg * inv) // 255
    b = (sb * 255 + db * inv) // 255
    a = sa + (da * inv) // 255
    return (r, g, b, a)

def rasterize_tri(base_img: Image.Image,
                  dest_tri: List[Tuple[float,float]],
                  src_img: Image.Image,
                  src_tri: List[Tuple[float,float]]):
    (x0, y0), (x1, y1), (x2, y2) = dest_tri
    (u0, v0), (u1, v1), (u2, v2) = src_tri

    def edge(ax, ay, bx, by, px, py): return (px - ax)*(by - ay) - (py - ay)*(bx - ax)

    area = edge(x0, y0, x1, y1, x2, y2)
    if area == 0: return
    minx = max(0, int(math.floor(min(x0, x1, x2))))
    maxx = min(base_img.width - 1, int(math.ceil(max(x0, x1, x2))))
    miny = max(0, int(math.floor(min(y0, y1, y2))))
    maxy = min(base_img.height - 1, int(math.ceil(max(y0, y1, y2))))

    pix = base_img.load()
    for iy in range(miny, maxy + 1):
        for ix in range(minx, maxx + 1):
            px = ix + 0.5; py = iy + 0.5
            w0 = edge(x1, y1, x2, y2, px, py)
            w1 = edge(x2, y2, x0, y0, px, py)
            w2 = edge(x0, y0, x1, y1, px, py)
            if (area > 0 and (w0 < 0 or w1 < 0 or w2 < 0)) or (area < 0 and (w0 > 0 or w1 > 0 or w2 > 0)):
                continue
            lam0 = w0 / area; lam1 = w1 / area; lam2 = w2 / area
            su = lam0*u0 + lam1*u1 + lam2*u2
            sv = lam0*v0 + lam1*v1 + lam2*v2
            src_rgba = sample_bilinear(src_img, su, sv)
            if src_rgba[3] == 0: continue
            pix[ix, iy] = alpha_over(pix[ix, iy], src_rgba)

def paste_sprite_rect(tex_img: Image.Image, spr, repl_img: Image.Image):
    tr = spr.m_RD.textureRect if getattr(spr, "m_RD", None) else spr.m_Rect
    x = round_int(tr.x); y = round_int(tr.y); w = round_int(tr.width); h = round_int(tr.height)
    if w <= 0 or h <= 0: return False
    H = tex_img.height; top = H - (y + h); left = x
    left = max(0, min(left, tex_img.width - w)); top = max(0, min(top, tex_img.height - h))
    if repl_img.size != (w, h): repl_img = repl_img.resize((w, h), resample=Image.LANCZOS)
    tex_img.alpha_composite(repl_img.convert("RGBA"), (left, top)); return True

def paste_sprite_mesh(tex_img: Image.Image, spr, repl_img: Image.Image):
    positions, uvs, indices = get_sprite_mesh(spr)
    if not uvs or not indices: return False
    tr = spr.m_RD.textureRect if getattr(spr, "m_RD", None) else spr.m_Rect
    rx, ry = float(tr.x), float(tr.y)
    rw, rh = max(1.0, float(tr.width)), max(1.0, float(tr.height))
    if repl_img.size != (int(round(rw)), int(round(rh))):
        repl_img = repl_img.resize((int(round(rw)), int(round(rh))), resample=Image.LANCZOS)
    W, H = tex_img.width, tex_img.height
    dest_pts: List[Tuple[float, float]] = []
    src_pts: List[Tuple[float, float]] = []
    for (u, v) in uvs:
        dx = u * W; dy = H - v * H; dest_pts.append((dx, dy))
        px = u * W - rx; py_bottom = v * H - ry
        sx = max(0.0, min(px, rw - 1.0)); sy = max(0.0, min(rh - py_bottom, rh - 1.0))
        src_pts.append((sx, sy))
    for i in range(0, len(indices), 3):
        i0, i1, i2 = indices[i], indices[i+1], indices[i+2]
        if i0 >= len(dest_pts) or i1 >= len(dest_pts) or i2 >= len(dest_pts): continue
        dtri = [dest_pts[i0], dest_pts[i1], dest_pts[i2]]
        stri = [src_pts[i0], src_pts[i1], src_pts[i2]]
        rasterize_tri(tex_img, dtri, repl_img, stri)
    return True

PID_RE = re.compile(r"__pid(-?\d+)$", re.IGNORECASE)

def collect_replacement_images(images_root: str):
    by_name, by_pid = {}, {}
    for path in glob.glob(os.path.join(images_root, "**", "*.png"), recursive=True):
        base = os.path.splitext(os.path.basename(path))[0]
        m = PID_RE.search(base)
        if m:
            try:
                pid = int(m.group(1)); by_pid[pid] = path
                main = base[:m.start()]
                if main and main not in by_name: by_name[main] = path
                continue
            except: pass
        by_name[base] = path
    return by_name, by_pid

def has_streamed_texture_data(textures) -> bool:
    for texture in textures:
        texture_data = texture
        stream_data = getattr(texture_data, "m_StreamData", None)
        if stream_data is None and hasattr(texture, "read"):
            try:
                texture_data = texture.read()
                stream_data = getattr(texture_data, "m_StreamData", None)
            except Exception:
                continue
        if stream_data and getattr(stream_data, "path", ""):
            return True
    return False

def resolve_stream_output_dir(bundle_path: str, out_path: str = None) -> str:
    bundle_stem = os.path.splitext(os.path.basename(bundle_path))[0]
    if out_path:
        base_dir = os.path.dirname(out_path) or "."
        out_stem = os.path.splitext(os.path.basename(out_path))[0]
        if out_stem and out_stem != bundle_stem:
            base_dir = os.path.join(base_dir, out_stem)
    else:
        base_dir = os.path.join(os.path.dirname(bundle_path) or ".", f"{bundle_stem}_patched")
    ensure_dir(base_dir)
    return os.path.abspath(base_dir)

def save_bundle_env(env, bundle_path: str, out_path: str, textures, done_label: str = "[DONE]"):
    if has_streamed_texture_data(textures):
        out_dir = resolve_stream_output_dir(bundle_path, out_path)
        for name in os.listdir(out_dir):
            path = os.path.join(out_dir, name)
            if not os.path.isfile(path):
                continue
            lower_name = name.lower()
            if lower_name.endswith(".bundle") or lower_name.endswith(".ress"):
                try:
                    os.remove(path)
                except OSError:
                    pass
        print(f"[SAVE] 检测到外部流资源，使用 env.save 输出到目录：{out_dir}")
        env.save(out_path=out_dir)
        desired_bundle_name = os.path.basename(out_path) if out_path else os.path.basename(bundle_path)
        if desired_bundle_name:
            bundle_files = [
                name for name in os.listdir(out_dir)
                if os.path.isfile(os.path.join(out_dir, name)) and name.lower().endswith(".bundle")
            ]
            if bundle_files:
                bundle_files.sort(key=lambda name: os.path.getmtime(os.path.join(out_dir, name)), reverse=True)
                src = os.path.join(out_dir, bundle_files[0])
                dst = os.path.join(out_dir, desired_bundle_name)
                if os.path.normcase(src) != os.path.normcase(dst) and os.path.isfile(dst):
                    try:
                        os.remove(dst)
                    except OSError:
                        pass
                if os.path.normcase(src) != os.path.normcase(dst):
                    try:
                        shutil.move(src, dst)
                    except OSError:
                        pass
                for extra_name in bundle_files[1:]:
                    try:
                        os.remove(os.path.join(out_dir, extra_name))
                    except OSError:
                        pass
        print(f"{done_label} 已写出 bundle 及其 .resS 到目录：{out_dir}")
        return

    if not out_path:
        root, ext = os.path.splitext(bundle_path)
        out_path = root + "_patched" + (ext or "")
    with open(out_path, "wb") as f:
        f.write(env.file.save(packer="original"))
    print(f"{done_label} 写出新 AB：{out_path}")

def import_sprites(bundle_path: str, images_root: str, out_path: str = None, mode: str = "rect", match: str = "name"):
    env = UnityPy.load(bundle_path)

    textures_by_pid = {}
    for obj in env.objects:
        if obj.type.name == "Texture2D":
            try: textures_by_pid[obj.path_id] = obj.read()
            except: pass

    sprites_group_by_tex = defaultdict(list)  # tex_pid -> [(spr, spr_pid)]
    for obj in env.objects:
        if obj.type.name != "Sprite":
            continue
        try:
            spr = obj.read()
        except Exception:
            continue
        tex_ptr = spr.m_RD.texture
        if tex_ptr and tex_ptr.path_id in textures_by_pid:
            sprites_group_by_tex[tex_ptr.path_id].append((spr, obj.path_id))

    name_map, pid_map = collect_replacement_images(images_root)
    if not name_map and not pid_map:
        print(f"[IMPORT] 未在 {images_root} 找到任何 *.png，结束"); return

    def choose_path(spr, pid):
        if match == "name":
            return name_map.get(spr.m_Name)
        elif match == "pid":
            return pid_map.get(pid)
        else:
            return pid_map.get(pid) or name_map.get(spr.m_Name)

    def paste(tex_img, spr, repl_img):
        return paste_sprite_mesh(tex_img, spr, repl_img) if mode == "mesh" else paste_sprite_rect(tex_img, spr, repl_img)

    changed_textures = 0
    for tex_pid, sprite_list in sprites_group_by_tex.items():
        tex = textures_by_pid[tex_pid]
        base_img = tex.image.convert("RGBA")
        applied = 0
        for spr, pid in sprite_list:
            path = choose_path(spr, pid)
            if not path: continue
            try:
                repl = Image.open(path).convert("RGBA")
                if paste(base_img, spr, repl): applied += 1
            except Exception as e:
                print(f"[ERR] 贴回 {spr.m_Name} 失败：{e}")
        if applied:
            tex.set_image(base_img)
            tex.save()
            changed_textures += 1
            print(f"[IMPORT-{mode}|{match}] 纹理 {tex.m_Name or tex_pid} 应用 {applied} 张替换图")

    if changed_textures == 0:
        print("[IMPORT] 没有任何纹理被修改，未输出新包")
        return

    save_bundle_env(env, bundle_path, out_path, textures_by_pid.values())

    # if changed_textures == 0:
    #     print("[IMPORT] 没有任何纹理被修改，未输出新包"); return
    # if not out_path:
    #     root, ext = os.path.splitext(bundle_path); out_path = root + "_patched" + (ext or "")
    # with open(out_path, "wb") as f:
    #     f.write(env.file.save(packer="original"))
    # print(f"[DONE] 写出新 AB：{out_path}")

def import_clip_folders(bundle_path: str, export_root: str, out_path: str = None):
    clip_dirs = []
    for entry in sorted(os.listdir(export_root)):
        clip_dir = os.path.join(export_root, entry)
        if not os.path.isdir(clip_dir):
            continue
        json_path = os.path.join(clip_dir, "clip.json")
        if not os.path.isfile(json_path):
            continue
        with open(json_path, "r", encoding="utf-8-sig") as f:
            meta = json.load(f)
        meta, changed = sync_clip_meta_with_pngs(clip_dir, meta)
        if changed:
            with open(json_path, "w", encoding="utf-8") as f:
                json.dump(meta, f, ensure_ascii=False, indent=2)
            print(f"[IMPORT-CLIPS] 已按 PNG 数量同步 clip.json: {clip_dir}")
        clip_dirs.append((clip_dir, meta))

    if not clip_dirs:
        print(f"[IMPORT-CLIPS] 在 {export_root} 未找到任何 clip.json")
        return

    if not out_path:
        root, ext = os.path.splitext(os.path.basename(bundle_path))
        out_path = os.path.join(export_root, f"{root}_patched{ext or '.bundle'}")

    env = UnityPy.load(bundle_path)
    animations = load_animations(env)
    plans = {}

    for clip_dir, meta in clip_dirs:
        clip_name = (meta.get("clipName") or os.path.basename(clip_dir)).strip().lower()
        clip_path_id = to_int(meta.get("clipPathId"), 0)
        animation = animations.get(clip_name)
        if animation is None and clip_path_id:
            for candidate in animations.values():
                if candidate.animation_clip.path_id == clip_path_id:
                    animation = candidate
                    break
        if animation is None:
            print(f"[IMPORT-CLIPS] 跳过未匹配动画: {clip_name}")
            continue

        keyframes = meta.get("keyframes") or []
        frames = meta.get("frames") or []
        if not keyframes and frames:
            frame_duration = derive_frame_duration(meta, frames, keyframes)
            keyframes = [
                {
                    "index": idx,
                    "time": frame_duration * idx,
                    "file": frame.get("file") or "",
                }
                for idx, frame in enumerate(frames)
            ]

        resolved_frames = []
        missing = False
        for idx, keyframe in enumerate(keyframes):
            file_name = keyframe.get("file") or ""
            if not file_name and idx < len(frames):
                file_name = frames[idx].get("file") or ""
            image_path = os.path.join(clip_dir, file_name) if file_name else ""
            if not image_path or not os.path.isfile(image_path):
                print(f"[IMPORT-CLIPS] 缺少帧图片: {os.path.join(clip_dir, file_name) if file_name else clip_dir}")
                missing = True
                break
            resolved_frames.append({
                "index": idx,
                "time": to_float(keyframe.get("time"), idx),
                "file": file_name,
                "image_path": image_path,
                "frame_meta": frames[idx] if idx < len(frames) else {},
            })

        if missing or not resolved_frames:
            print(f"[IMPORT-CLIPS] 跳过无有效帧的动画目录: {clip_dir}")
            continue

        plans[clip_name] = {
            "clip_dir": clip_dir,
            "meta": meta,
            "clip_name": clip_name,
            "clip_path_id": animation.animation_clip.path_id,
            "original_count": len(animation.sprites),
            "target_count": len(resolved_frames),
            "frames": resolved_frames,
        }

    if not plans:
        print("[IMPORT-CLIPS] 没有任何动画可更新，未输出新包")
        return

    updates = []
    for clip_name, plan in plans.items():
        animation = animations.get(clip_name)
        if animation is None:
            continue
        if plan["target_count"] != len(animation.sprites):
            updates.append({
                "anim": animation,
                "expected": plan["target_count"],
            })

    temp_file = None
    work_bundle_path = bundle_path
    sprite_asset_changes = []
    if updates:
        fd, temp_file = tempfile.mkstemp(prefix="rml_sprite_anim_", suffix=".bundle")
        os.close(fd)
        try:
            sprite_asset_changes = update_sprites(bundle_path, updates, temp_file)
            work_bundle_path = temp_file
        except Exception:
            if temp_file and os.path.isfile(temp_file):
                os.remove(temp_file)
            raise

    try:
        env = UnityPy.load(work_bundle_path)
        animations = load_animations(env)

        if sprite_asset_changes:
            for anim_name, is_add, changed_ids in sprite_asset_changes:
                animation = animations.get(anim_name)
                if animation is None:
                    continue

                anim_data = animation.animation_clip_data
                anim_pptr_mapping = anim_data.get("m_ClipBindingConstant", {}).get("pptrCurveMapping", [])
                mono_data = animation.mono_behaviour.parse_as_dict() if animation.mono_behaviour is not None else None
                mono_sprites = mono_data.get("sprites", []) if mono_data is not None else None

                if is_add:
                    pptr_template = dict(anim_pptr_mapping[-1]) if anim_pptr_mapping else {"m_FileID": 0, "m_PathID": 0}
                    mono_template = dict(mono_sprites[-1]) if mono_sprites else {"m_FileID": 0, "m_PathID": 0}
                    for path_id in changed_ids:
                        clone = dict(pptr_template)
                        clone["m_PathID"] = int(path_id)
                        anim_pptr_mapping.append(clone)
                        if mono_sprites is not None:
                            mono_clone = dict(mono_template)
                            mono_clone["m_PathID"] = int(path_id)
                            mono_sprites.append(mono_clone)
                else:
                    removed = set(int(path_id) for path_id in changed_ids)
                    anim_data["m_ClipBindingConstant"]["pptrCurveMapping"] = [
                        entry for entry in anim_pptr_mapping if to_int(entry.get("m_PathID"), 0) not in removed
                    ]
                    if mono_sprites is not None:
                        mono_data["sprites"] = [
                            entry for entry in mono_sprites if to_int(entry.get("m_PathID"), 0) not in removed
                        ]

                animation.animation_clip.save_typetree(anim_data)
                if mono_data is not None:
                    animation.mono_behaviour.save_typetree(mono_data)

            with open(work_bundle_path, "wb") as f:
                f.write(env.file.save(packer="original"))
            env = UnityPy.load(work_bundle_path)
            animations = load_animations(env)

        anim_id_to_playable_asset_id, playable_asset_id_to_track_clip, tracks = collect_track_bindings(env)

        all_sprites_by_texture = defaultdict(list)
        for obj in env.objects:
            if obj.type.name != "Sprite":
                continue
            try:
                texture_pid = to_int(obj.parse_as_dict()["m_RD"]["texture"]["m_PathID"], 0)
                all_sprites_by_texture[texture_pid].append(obj)
            except Exception:
                pass

        texture_plans = {}
        updated_animations = 0

        for clip_name, plan in plans.items():
            animation = animations.get(clip_name)
            if animation is None or not animation.sprites:
                print(f"[IMPORT-CLIPS] 资产更新后找不到动画: {clip_name}")
                continue

            sprite_slots = animation.sprites[:plan["target_count"]]
            if len(sprite_slots) < plan["target_count"]:
                print(f"[IMPORT-CLIPS] 动画帧数不足: {clip_name}，期望 {plan['target_count']} 实际 {len(sprite_slots)}")
                continue

            path_ids = [sprite.path_id for _, sprite, _ in sprite_slots]
            anim_data = animation.animation_clip_data
            existing_mapping = anim_data.get("m_ClipBindingConstant", {}).get("pptrCurveMapping", [])
            anim_data["m_ClipBindingConstant"]["pptrCurveMapping"] = build_pointer_array(existing_mapping, path_ids)

            delta = plan["target_count"] - plan["original_count"]
            if delta != 0:
                anim_data["m_MuscleClipSize"] += delta * 7 * 4
                value_array_delta = anim_data.get("m_MuscleClip", {}).get("m_ValueArrayDelta", [])
                if value_array_delta:
                    value_array_delta[0]["m_Stop"] = to_float(value_array_delta[0].get("m_Stop"), 0.0) + delta

            sample_rate = to_float(anim_data.get("m_SampleRate"), to_float(plan["meta"].get("sampleRate"), 12.0))
            frame_duration = derive_frame_duration(plan["meta"], plan["meta"].get("frames") or [], plan["meta"].get("keyframes") or [])
            clip_length = to_float(plan["meta"].get("length"), 0.0)
            if clip_length <= 0:
                clip_length = frame_duration * plan["target_count"]
            elif frame_duration > 0 and plan["target_count"] > 0:
                clip_length = frame_duration * plan["target_count"]
            anim_data["m_MuscleClip"]["m_StopTime"] = clip_length
            anim_data["m_MuscleClip"]["m_Clip"]["data"]["m_DenseClip"]["m_FrameCount"] = plan["target_count"] + 2
            ensure_streamed_clip_data(anim_data, plan["frames"], sample_rate)
            animation.animation_clip.save_typetree(anim_data)

            if animation.mono_behaviour is not None:
                mono_data = animation.mono_behaviour.parse_as_dict()
                mono_data["sprites"] = build_pointer_array(mono_data.get("sprites", []), path_ids)
                animation.mono_behaviour.save_typetree(mono_data)

            playable_asset_id = anim_id_to_playable_asset_id.get(animation.animation_clip.path_id)
            if playable_asset_id is not None:
                track_clip = playable_asset_id_to_track_clip.get(playable_asset_id)
                if track_clip is not None:
                    track_clip["m_Duration"] = clip_length

            if animation.texture is None:
                print(f"[IMPORT-CLIPS] 动画缺少贴图引用: {clip_name}")
                continue

            texture_pid = animation.texture.path_id
            texture_plan = texture_plans.get(texture_pid)
            if texture_plan is None:
                texture_plan = {
                    "texture_obj": animation.texture,
                    "frames": [],
                    "all_sprites": list(all_sprites_by_texture.get(texture_pid, [])),
                }
                texture_plans[texture_pid] = texture_plan

            for index, frame in enumerate(plan["frames"]):
                sprite_obj = sprite_slots[index][1]
                sprite_data = sprite_obj.parse_as_dict()
                slot_rect = dict(sprite_data["m_RD"]["textureRect"])
                texture_plan["frames"].append({
                    "sprite_obj": sprite_obj,
                    "image_path": frame["image_path"],
                    "rect": slot_rect,
                    "is_added": index >= plan["original_count"],
                    "pivot01": (frame.get("frame_meta") or {}).get("pivot01"),
                    "border": (frame.get("frame_meta") or {}).get("border"),
                })

            updated_animations += 1
            print(f"[IMPORT-CLIPS] 已重建动画绑定: {clip_name}，目标帧数 {plan['target_count']}")

        for obj, mono_data in tracks:
            obj.save_typetree(mono_data)

        changed_textures = 0
        for texture_pid, texture_plan in texture_plans.items():
            if not texture_plan["frames"]:
                continue

            texture_data = texture_plan["texture_obj"].read()
            base_image = texture_data.image.convert("RGBA")
            old_w, old_h = base_image.size
            target_frames = texture_plan["frames"]
            target_sprite_ids = {entry["sprite_obj"].path_id for entry in target_frames}

            padding = 2
            packed_frames = []
            frame_sizes = []
            for entry in target_frames:
                repl = Image.open(entry["image_path"]).convert("RGBA")
                frame_w, frame_h = repl.size
                frame_sizes.append((frame_w, frame_h))
                packed_frames.append((entry, repl, frame_w, frame_h))

            preserve_existing = len(target_sprite_ids) != len(texture_plan["all_sprites"])
            atlas_width, grid_h, placements = choose_grid_layout(
                frame_sizes,
                padding,
                min_width=old_w if preserve_existing else 0,
                base_height=old_h if preserve_existing else 0,
            )
            if preserve_existing:
                new_h = old_h + grid_h
                expanded = Image.new("RGBA", (atlas_width, new_h), (0, 0, 0, 0))
                expanded.alpha_composite(base_image, (0, grid_h))
                base_image = expanded
                for entry in target_frames:
                    clear_sprite_uv_region(base_image, entry["sprite_obj"], (old_w, old_h), grid_h)
            else:
                new_h = grid_h
                base_image = Image.new("RGBA", (atlas_width, new_h), (0, 0, 0, 0))

            for sprite_obj in texture_plan["all_sprites"]:
                if sprite_obj.path_id in target_sprite_ids:
                    continue
                sprite_data = sprite_obj.parse_as_dict()
                old_rect = dict(sprite_data["m_RD"]["textureRect"])
                remap_sprite_uvs(sprite_data, (old_w, old_h), (atlas_width, new_h), old_rect, old_rect)
                sprite_obj.save_typetree(sprite_data)

            for (entry, repl, rect_w, rect_h), (left, top, _, _) in zip(packed_frames, placements):
                sprite_obj = entry["sprite_obj"]
                sprite_data = sprite_obj.parse_as_dict()
                new_rect = {
                    "x": left,
                    "y": new_h - (top + rect_h),
                    "width": rect_w,
                    "height": rect_h,
                }

                pivot01 = entry.get("pivot01")
                if isinstance(pivot01, list) and len(pivot01) >= 2:
                    sprite_data["m_Pivot"]["x"] = to_float(pivot01[0], sprite_data["m_Pivot"].get("x", 0.5))
                    sprite_data["m_Pivot"]["y"] = to_float(pivot01[1], sprite_data["m_Pivot"].get("y", 0.5))

                border = entry.get("border")
                if isinstance(border, list) and len(border) >= 4:
                    sprite_data["m_Border"]["x"] = to_float(border[0], 0.0)
                    sprite_data["m_Border"]["y"] = to_float(border[1], 0.0)
                    sprite_data["m_Border"]["z"] = to_float(border[2], 0.0)
                    sprite_data["m_Border"]["w"] = to_float(border[3], 0.0)

                build_rect_sprite_geometry(
                    sprite_data,
                    (atlas_width, new_h),
                    new_rect,
                    sprite_data.get("m_PixelsToUnits", 100.0),
                    pivot01,
                )

                sprite_obj.save_typetree(sprite_data)
                entry["rect"] = new_rect
                entry["image"] = repl

            for entry in texture_plan["frames"]:
                try:
                    composite_image_to_rect(base_image, entry["rect"], entry.get("image") or Image.open(entry["image_path"]).convert("RGBA"))
                except Exception as ex:
                    print(f"[IMPORT-CLIPS] 贴图写入失败: {entry['image_path']} -> {ex}")

            texture_data.set_image(base_image)
            texture_data.save()
            changed_textures += 1
            print(f"[IMPORT-CLIPS] 已更新纹理 {texture_pid}，应用 {len(texture_plan['frames'])} 帧")

        if updated_animations <= 0 or changed_textures <= 0:
            print("[IMPORT-CLIPS] 没有任何动画或纹理被更新，未输出新包")
            return

        save_bundle_env(
            env,
            bundle_path,
            out_path,
            [texture_plan["texture_obj"] for texture_plan in texture_plans.values()],
        )
    finally:
        if temp_file and os.path.isfile(temp_file):
            try:
                os.remove(temp_file)
            except Exception:
                pass

def main():
    ap = argparse.ArgumentParser(description="导出/回填 AnimationClip 引用的 Sprites（支持压缩曲线/mesh_blit，匹配方式可选）")
    sub = ap.add_subparsers(dest="cmd")

    ap_scan = sub.add_parser("scan", help="统计包内对象类型数量")
    ap_scan.add_argument("bundle")

    ap_exp = sub.add_parser("export", help="导出 Sprites 到 动画名/精灵名.png（支持压缩曲线映射）")
    ap_exp.add_argument("bundle"); ap_exp.add_argument("out_dir")
    ap_exp.add_argument("--all-sprites", action="store_true", help="忽略动画引用，导出全部 Sprite 兜底")
    ap_exp.add_argument("--unique-names", action="store_true", help="文件名追加 __pid<id> 避免同名冲突")
    ap_exp.add_argument("--group-by-texture", action="store_true", help="导出全部时按 Texture2D 名称分组")

    ap_imp = sub.add_parser("import", help="按名称/路径ID/自动匹配回填，支持 mesh_blit")
    ap_imp.add_argument("bundle"); ap_imp.add_argument("images_root")
    ap_imp.add_argument("-o", "--output")
    ap_imp.add_argument("--mode", choices=["rect", "mesh"], default="rect", help="回填方式：rect=矩形；mesh=网格逐三角")
    ap_imp.add_argument("--match", choices=["name", "pid", "auto"], default="name", help="匹配方式：name=按 sprite 名；pid=按 __pid 后缀；auto=pid优先后名")

    ap_imp_clips = sub.add_parser("import-clips", help="按导出的 clip.json + PNG 回填动画，并输出原文件名_patched.bundle")
    ap_imp_clips.add_argument("bundle")
    ap_imp_clips.add_argument("export_root")
    ap_imp_clips.add_argument("-o", "--output")

    args = ap.parse_args()
    if args.cmd == "scan":
        scan_bundle(args.bundle)
    elif args.cmd == "export":
        export_sprites(args.bundle, args.out_dir, args.all_sprites, args.unique_names, args.group_by_texture)
    elif args.cmd == "import":
        import_sprites(args.bundle, args.images_root, args.output, args.mode, args.match)
    elif args.cmd == "import-clips":
        try:
            import_clip_folders(args.bundle, args.export_root, args.output)
        except Exception:
            traceback.print_exc()
            raise
    else:
        ap.print_help()

if __name__ == "__main__":
    main()



