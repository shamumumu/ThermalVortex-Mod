extends Node2D
## The C# owner supplies time. This player never advances or restores a live actor.

const DURATION := 2.1
const ENTRY_START := 0.75
const BODY_END := 1.6
const NECK_FINISH := BODY_END - 0.08
const PINCH_START := 2.0
const PROFILE_STEP := 4
const BODY_SHADER = preload("body_recall.gdshader")
const RIFT_SHADER = preload("rift_layer.gdshader")
const ACCENTS_SCRIPT = preload("recall_accents.gd")

var _body_frames: Array[Texture2D] = []
var _rift_frames: Array[Texture2D] = []
var _body_profiles: Array[Dictionary] = []
var _rift_profiles: Array[Dictionary] = []
var _foot_anchors: Array[Vector2] = []
var _scale_multipliers: Array[float] = []
var _body: Sprite2D
var _back: Sprite2D
var _front: Sprite2D
var _accents: Node2D
var _body_material: ShaderMaterial
var _back_material: ShaderMaterial
var _initial_texture: Texture2D
var _initial_transform := Transform2D.IDENTITY
var _foot_origin := Vector2.ZERO
var _height := 0.0
var _body_scale := 1.0
var _facing := 1.0
var _rift_scale := 1.0
var _rift_anchor := Vector2.ZERO
var _rift_center := Vector2.ZERO
var _travel_end := 0.0
var _travel_neck := 0.0
var _lift_ratio := -0.06
var _cleanup_time := BODY_END
var _cleanup_reason := "complete_entry"
var _cleanup_fraction := 0.0
var _configured := false
var _sample: Dictionary = {}


func setup(body_frames: Array[Texture2D], rift_frames: Array[Texture2D], config: Dictionary,
        initial_texture: Texture2D, initial_transform: Transform2D) -> bool:
    clear()
    if body_frames.size() != 36 or rift_frames.size() != 25:
        return false
    _height = float(config.get("target_height", 0.0))
    var reference_height := float(config.get("body_reference_height", 0.0))
    var raw_anchors: Array = config.get("body_foot_anchors", [])
    var raw_scales: Array = config.get("body_scale_multipliers", [])
    if not is_finite(_height) or _height <= 0.0 or not is_finite(reference_height) or reference_height <= 0.0:
        return false
    if raw_anchors.size() != 36 or raw_scales.size() != 36 or initial_texture == null:
        return false
    if not (config.get("foot_origin", null) is Vector2) or not initial_transform.is_finite():
        return false
    if absf(initial_transform.determinant()) < 0.000001:
        return false
    _foot_origin = config["foot_origin"]
    if not _foot_origin.is_finite():
        return false
    _facing = -1.0 if float(config.get("facing_sign", 1.0)) < 0.0 else 1.0
    _lift_ratio = float(config.get("lift_ratio", -0.06))
    if not is_finite(_lift_ratio) or absf(_lift_ratio) > 0.25:
        return false
    _body_scale = _height / reference_height
    for index in range(36):
        if body_frames[index] == null or not (raw_anchors[index] is Vector2):
            clear()
            return false
        var multiplier := float(raw_scales[index])
        var anchor: Vector2 = raw_anchors[index]
        if not is_finite(multiplier) or multiplier <= 0.0 or not anchor.is_finite():
            clear()
            return false
        _foot_anchors.append(anchor)
        _scale_multipliers.append(multiplier)
        var profile := _read_profile(body_frames[index], false)
        if profile.is_empty():
            clear()
            return false
        _body_profiles.append(profile)
    for texture in rift_frames:
        if texture == null:
            clear()
            return false
        var profile := _read_profile(texture, true)
        if profile.is_empty():
            clear()
            return false
        _rift_profiles.append(profile)
    _body_frames.assign(body_frames)
    _rift_frames.assign(rift_frames)
    _initial_texture = initial_texture
    _initial_transform = initial_transform
    var opening_bounds: Rect2 = _rift_profiles[14]["bounds"]
    _rift_scale = _height * 1.2 / opening_bounds.size.y
    _rift_anchor = opening_bounds.get_center()
    _rift_center = Vector2(0.0, -_height * 0.5)
    _place_portal_and_measure_travel()
    _measure_cleanup_time()
    _ensure_sprites()
    _configured = true
    seek(0.0)
    return true


func seek(time_seconds: float) -> bool:
    if not _configured:
        return true
    var time := clampf(time_seconds, 0.0, DURATION) if is_finite(time_seconds) else DURATION
    var body_index := _body_frame_at_time(time)
    var rift_index := 14
    if time < ENTRY_START:
        rift_index = mini(14, int(floor(time / ENTRY_START * 15.0)))
    elif time >= BODY_END:
        rift_index = mini(24, 15 + int(floor((time - BODY_END) / (PINCH_START - BODY_END) * 10.0)))
    var travel := _travel_at_time(time)
    var lift := _height * _lift_ratio * smoothstep(ENTRY_START, NECK_FINISH, time)
    _body.texture = _body_frames[body_index]
    _body.offset = -_foot_anchors[body_index]
    var body_scale := _body_scale * _scale_multipliers[body_index]
    _body.scale = Vector2(_facing * body_scale, body_scale)
    _body.position = _foot_origin + Vector2(-_facing * travel, -lift)
    _body.visible = time < _cleanup_time
    _body_material.set_shader_parameter("mask_active", time >= ENTRY_START and time < BODY_END)
    _body_material.set_shader_parameter("initial_weight", 1.0 - smoothstep(0.0, 0.10, time))
    var rim_strength := smoothstep(0.08, 0.34, time) * (0.62 + 0.30 * smoothstep(ENTRY_START, 1.35, time))
    rim_strength *= 1.0 - smoothstep(NECK_FINISH, BODY_END, time)
    _body_material.set_shader_parameter("rim_light_strength", rim_strength)
    var rift_opacity := smoothstep(0.0, 0.07, time) * (1.0 - smoothstep(PINCH_START, DURATION, time))
    var pinch := lerpf(1.0, 0.08, smoothstep(PINCH_START, DURATION, time))
    _back.texture = _rift_frames[rift_index]
    _back.offset = -_rift_anchor
    _back.scale = Vector2(_facing * _rift_scale * pinch, _rift_scale)
    _back.position = _foot_origin + Vector2(_facing * _rift_center.x, _rift_center.y)
    _back.modulate = Color(1.0, 1.0, 1.0, rift_opacity)
    _back.visible = time < DURATION
    _front.visible = false
    _accents.position = _foot_origin + Vector2(_facing * _rift_center.x, _rift_center.y)
    _accents.scale = Vector2(_facing * _height, _height)
    _accents.call("seek", time)
    var mask_active := time >= ENTRY_START and time < BODY_END
    _body_material.set_shader_parameter("portal_contour", _rift_profiles[14]["texture"])
    _body_material.set_shader_parameter("portal_size", _rift_frames[14].get_size())
    _set_body_mappings()
    _sample = {
        "time": time, "duration": DURATION, "body_frame": body_index,
        "rift_source_frame": rift_index + 4, "body_visible": _body.visible,
        "rift_visible": time < DURATION and rift_opacity > 0.0,
        "mask_active": mask_active, "travel": travel, "lift": lift,
        "mask_edge": "left_rim_inner", "body_over_rift": true, "rift_front_visible": false,
        "cleanup_time": _cleanup_time, "cleanup_reason": _cleanup_reason,
        "cleanup_visible_fraction": _cleanup_fraction,
        "travel_progress": travel / maxf(_travel_end, 0.00001),
        "travel_end": _travel_end, "neck_finish_start": NECK_FINISH,
        "cutline_x": _foot_origin.x + _facing * (_rift_center.x + _gate_at_y(-_height * 0.5)),
        "portal_height": _height * 1.2, "portal_center": _foot_origin + Vector2(_facing * _rift_center.x, _rift_center.y),
        "configured": true, "complete": time >= DURATION
    }
    return time >= DURATION


func clear() -> void:
    _configured = false
    if is_instance_valid(_accents):
        _accents.call("clear")
    for sprite in [_body, _back, _front]:
        if is_instance_valid(sprite):
            sprite.visible = false
            sprite.texture = null
            sprite.material = null
    _body_frames.clear()
    _rift_frames.clear()
    _body_profiles.clear()
    _rift_profiles.clear()
    _foot_anchors.clear()
    _scale_multipliers.clear()
    _initial_texture = null
    _body_material = null
    _back_material = null
    _cleanup_time = BODY_END
    _cleanup_reason = "complete_entry"
    _cleanup_fraction = 0.0
    _sample = {"configured": false, "body_visible": false, "rift_visible": false, "complete": true}


func get_sample_info() -> Dictionary:
    return _sample.duplicate(true)


func _ensure_sprites() -> void:
    _back = _get_sprite("RiftBack", 0)
    _body = _get_sprite("Body", 1)
    _front = _get_sprite("RiftFront", 2)
    _body_material = ShaderMaterial.new()
    _body_material.shader = BODY_SHADER
    _body_material.set_shader_parameter("initial_texture", _initial_texture)
    _body_material.set_shader_parameter("rim_light_width", 5.0)
    _body.material = _body_material
    _back_material = ShaderMaterial.new()
    _back_material.shader = RIFT_SHADER
    _back.material = _back_material
    if not is_instance_valid(_accents):
        _accents = Node2D.new()
        _accents.set_script(ACCENTS_SCRIPT)
        _accents.name = "RecallAccents"
        add_child(_accents)
    _accents.z_index = 0
    # The retained empty node keeps scene inspection stable. It never draws:
    # the user wants the actor to cover the entire rift until the LEFT exit.
    _front.visible = false
    _front.texture = null
    _front.material = null


func _get_sprite(node_name: String, order: int) -> Sprite2D:
    var sprite := get_node_or_null(NodePath(node_name)) as Sprite2D
    if sprite == null:
        sprite = Sprite2D.new()
        sprite.name = node_name
        add_child(sprite)
    sprite.centered = false
    sprite.z_index = order
    sprite.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR
    return sprite


func _set_body_mappings() -> void:
    var body_size := _body.texture.get_size()
    var body_origin := _body.transform * _body.offset
    var body_x := _body.transform * (_body.offset + Vector2(body_size.x, 0.0))
    var body_y := _body.transform * (_body.offset + Vector2(0.0, body_size.y))
    var portal_inverse := _back.transform.affine_inverse()
    var portal_size := _back.texture.get_size()
    var portal_origin := (portal_inverse * body_origin - _back.offset) / portal_size
    _body_material.set_shader_parameter("portal_uv_origin", portal_origin)
    _body_material.set_shader_parameter("portal_uv_x", (portal_inverse * body_x - _back.offset) / portal_size - portal_origin)
    _body_material.set_shader_parameter("portal_uv_y", (portal_inverse * body_y - _back.offset) / portal_size - portal_origin)
    var initial_inverse := _initial_transform.affine_inverse()
    var initial_size := _initial_texture.get_size()
    var initial_origin := (initial_inverse * body_origin) / initial_size
    _body_material.set_shader_parameter("initial_uv_origin", initial_origin)
    _body_material.set_shader_parameter("initial_uv_x", (initial_inverse * body_x) / initial_size - initial_origin)
    _body_material.set_shader_parameter("initial_uv_y", (initial_inverse * body_y) / initial_size - initial_origin)


func _place_portal_and_measure_travel() -> void:
    # Retain the established portal placement: its right side starts behind
    # the cape. The actor now travels across the full opening before reaching
    # the left exit. Turning its mask on cannot erase the stationary cape.
    var profile: Dictionary = _body_profiles[18]
    var points: Array = profile["rows"]
    var scale_factor := _body_scale * _scale_multipliers[18]
    var clearance := INF
    for row: Vector3 in points:
        var y := (row.x - _foot_anchors[18].y) * scale_factor
        var left := (row.y - _foot_anchors[18].x) * scale_factor
        var edges := _portal_edges_at_y(y)
        var source_width := (edges.y - edges.x) / _rift_scale
        var previous_inset := clampf(source_width * 0.13, 16.0, 48.0) * 0.5 * _rift_scale
        clearance = minf(clearance, left - (edges.y - previous_inset))
    _rift_center.x = clearance - _height * 0.012
    var maximum := 0.0
    var neck_minimum := INF
    for index in range(18, 36):
        profile = _body_profiles[index]
        scale_factor = _body_scale * _scale_multipliers[index]
        var bounds: Rect2 = profile["bounds"]
        var neck_end := INF
        for row: Vector3 in profile["rows"]:
            # Include the final vertical shift when measuring both the full
            # exit and the neck against the same moving geometry.
            var y := (row.x - _foot_anchors[index].y) * scale_factor - _height * _lift_ratio
            var right := (row.z - _foot_anchors[index].x) * scale_factor
            var distance := right - (_rift_center.x + _gate_at_y(y))
            maximum = maxf(maximum, distance)
            var anatomical_y := (row.x - bounds.position.y) / bounds.size.y
            if anatomical_y >= 0.25 and anatomical_y <= 0.30:
                # Preserve the connected neck band to the right of the left
                # exit until 1.42. No rift front layer can hide it early.
                neck_end = minf(neck_end, distance)
        if index >= 30:
            neck_minimum = minf(neck_minimum, neck_end)
    _travel_end = maximum + _height * 0.025
    # Keep the neck attached until the last 80 ms, then take the entire
    # remaining silhouette beyond the left edge instead of holding a head.
    _travel_neck = maxf(0.0, minf(_travel_end * 0.92, neck_minimum - _height * 0.022))


func _gate_at_y(canonical_y: float) -> float:
    var edges := _portal_edges_at_y(canonical_y)
    var width := (edges.y - edges.x) / _rift_scale
    return edges.x + (clampf(width * 0.13, 16.0, 48.0) + 1.0) * _rift_scale


func _body_frame_at_time(time: float) -> int:
    # Give the anticipation more time, then play the retreat and closure faster.
    if time < ENTRY_START:
        return mini(17, int(floor(time / ENTRY_START * 18.0)))
    return mini(35, 18 + int(floor((time - ENTRY_START) / (BODY_END - ENTRY_START) * 18.0)))


func _travel_at_time(time: float) -> float:
    if time < ENTRY_START:
        return 0.0
    if time < NECK_FINISH:
        var progress := (time - ENTRY_START) / (NECK_FINISH - ENTRY_START)
        return _travel_neck * progress * progress
    return lerpf(_travel_neck, _travel_end, clampf((time - NECK_FINISH) / (BODY_END - NECK_FINISH), 0.0, 1.0))


func _measure_cleanup_time() -> void:
    # Budget once against the actual alpha samples and actual moving gate.
    # Normal seek/render calls only compare time with this monotonic endpoint.
    _cleanup_time = BODY_END
    for step in range(ceili((BODY_END - ENTRY_START) * 60.0) + 1):
        var time := minf(ENTRY_START + float(step) / 60.0, BODY_END)
        var stats := _visible_body_stats(time)
        var fraction := float(stats["fraction"])
        if fraction < 0.02 or not bool(stats["connected_body"]):
            _cleanup_time = minf(time, BODY_END)
            _cleanup_fraction = fraction
            _cleanup_reason = "under_two_percent" if fraction < 0.02 else "only_disconnected_fragments"
            return


func _visible_body_stats(time: float) -> Dictionary:
    var index := _body_frame_at_time(time)
    var profile: Dictionary = _body_profiles[index]
    var points: PackedVector2Array = profile["points"]
    var alphas: PackedFloat32Array = profile["alphas"]
    var grid_indices: PackedInt32Array = profile["grid_indices"]
    var grid_size: Vector2i = profile["grid_size"]
    var scale_factor := _body_scale * _scale_multipliers[index]
    var lift := _height * _lift_ratio * smoothstep(ENTRY_START, NECK_FINISH, time)
    var travel := _travel_at_time(time)
    var gates := PackedFloat32Array()
    gates.resize(grid_size.y)
    for row in range(grid_size.y):
        var source_y := float(row * PROFILE_STEP) + 0.5
        var y := (source_y - _foot_anchors[index].y) * scale_factor - lift
        gates[row] = _rift_center.x + _gate_at_y(y)
    var active := PackedByteArray()
    active.resize(grid_size.x * grid_size.y)
    active.fill(0)
    var visible_area := 0.0
    var feather := 1.5 * _rift_scale
    for point_index in range(points.size()):
        var source_point := points[point_index]
        var grid_index := grid_indices[point_index]
        var row := int(grid_index / grid_size.x)
        var x := (source_point.x - _foot_anchors[index].x) * scale_factor - travel
        var keep := smoothstep(-feather, feather, x - gates[row])
        visible_area += alphas[point_index] * keep
        if alphas[point_index] * keep >= 0.2:
            active[grid_index] = 1
    var fraction := visible_area / maxf(float(profile["alpha_area"]), 0.00001)
    # A substantial remaining silhouette is not a cleanup candidate.
    if fraction > 0.18 or fraction < 0.02:
        return {"fraction": fraction, "connected_body": fraction >= 0.02}
    var bounds: Rect2 = profile["bounds"]
    var minimum_span := bounds.size.y * 0.16
    var minimum_component := maxi(3, int(points.size() * 0.012))
    var queue := PackedInt32Array()
    for start in range(active.size()):
        if active[start] != 1:
            continue
        queue.clear()
        queue.append(start)
        active[start] = 2
        var cursor := 0
        var min_y := INF
        var max_y := -INF
        var touches_torso := false
        while cursor < queue.size():
            var cell := queue[cursor]
            cursor += 1
            var grid_y := int(cell / grid_size.x)
            var grid_x := cell % grid_size.x
            var source_y := float(grid_y * PROFILE_STEP) + 0.5
            var source_x := float(grid_x * PROFILE_STEP) + 0.5
            min_y = minf(min_y, source_y)
            max_y = maxf(max_y, source_y)
            var anatomical_y := (source_y - bounds.position.y) / bounds.size.y
            var anatomical_x := (source_x - _foot_anchors[index].x) / bounds.size.y
            if anatomical_y >= 0.32 and anatomical_y <= 0.62 and anatomical_x >= -0.02 and anatomical_x <= 0.22:
                touches_torso = true
            for direction in [Vector2i(-1, 0), Vector2i(1, 0), Vector2i(0, -1), Vector2i(0, 1)]:
                var next_x: int = grid_x + direction.x
                var next_y: int = grid_y + direction.y
                if next_x < 0 or next_y < 0 or next_x >= grid_size.x or next_y >= grid_size.y:
                    continue
                var neighbor: int = next_y * grid_size.x + next_x
                if active[neighbor] == 1:
                    active[neighbor] = 2
                    queue.append(neighbor)
        if touches_torso and max_y - min_y >= minimum_span and queue.size() >= minimum_component:
            return {"fraction": fraction, "connected_body": true}
    return {"fraction": fraction, "connected_body": false}


func _portal_edges_at_y(canonical_y: float) -> Vector2:
    var source_y := (canonical_y - _rift_center.y) / _rift_scale + _rift_anchor.y
    var profile: Dictionary = _rift_profiles[14]
    var lefts: PackedFloat32Array = profile["lefts"]
    var rights: PackedFloat32Array = profile["rights"]
    # Lookup samples represent source pixel centers (0.5, 4.5, ...), exactly
    # matching the GPU's source-coordinate to contour-coordinate conversion.
    var coordinate := clampf((source_y - 0.5) / PROFILE_STEP, 0.0, float(lefts.size() - 1))
    var lower := int(floor(coordinate))
    var upper := mini(lower + 1, lefts.size() - 1)
    var left := lerpf(lefts[lower], lefts[upper], coordinate - lower)
    var right := lerpf(rights[lower], rights[upper], coordinate - lower)
    return Vector2(left - _rift_anchor.x, right - _rift_anchor.x) * _rift_scale


func _read_profile(texture: Texture2D, make_contour: bool) -> Dictionary:
    var source := texture.get_image()
    if source == null or source.is_empty():
        return {}
    if source.is_compressed() and source.decompress() != OK:
        return {}
    source.convert(Image.FORMAT_RGBA8)
    var size := source.get_size()
    var pixels := source.get_data()
    var rows: Array[Vector3] = []
    var points := PackedVector2Array()
    var alphas := PackedFloat32Array()
    var grid_indices := PackedInt32Array()
    var grid_size := Vector2i(ceili(float(size.x) / PROFILE_STEP), ceili(float(size.y) / PROFILE_STEP))
    var alpha_area := 0.0
    var lefts := PackedFloat32Array()
    var rights := PackedFloat32Array()
    var first_valid := -1
    var minimum := Vector2(INF, INF)
    var maximum := Vector2(-INF, -INF)
    for y in range(0, size.y, PROFILE_STEP):
        var left := -1
        var right := -1
        var best_left := -1
        var best_right := -1
        var gap := 0
        for x in range(0, size.x, PROFILE_STEP):
            if pixels[(y * size.x + x) * 4 + 3] >= 128:
                if left < 0:
                    left = x
                right = x
                gap = 0
            elif left >= 0:
                gap += 1
                if gap > 2:
                    if best_left < 0 or right - left > best_right - best_left:
                        best_left = left
                        best_right = right
                    left = -1
        if left >= 0 and (best_left < 0 or right - left > best_right - best_left):
            best_left = left
            best_right = right
        if not make_contour:
            # Body rows retain every limb and the cape, including disjoint
            # alpha runs; portal rows select the substantial central opening.
            best_left = -1
            best_right = -1
            for x in range(0, size.x, PROFILE_STEP):
                if pixels[(y * size.x + x) * 4 + 3] >= 48:
                    var alpha := float(pixels[(y * size.x + x) * 4 + 3]) / 255.0
                    points.append(Vector2(x + 0.5, y + 0.5))
                    alphas.append(alpha)
                    grid_indices.append(int(y / PROFILE_STEP) * grid_size.x + int(x / PROFILE_STEP))
                    alpha_area += alpha
                    if best_left < 0:
                        best_left = x
                    best_right = x
        if best_left >= 0 and best_right >= best_left:
            if first_valid < 0:
                first_valid = lefts.size()
            var expanded_left := maxf(0.0, best_left - PROFILE_STEP)
            var expanded_right := minf(size.x - 1.0, best_right + PROFILE_STEP)
            lefts.append(expanded_left)
            rights.append(expanded_right)
            rows.append(Vector3(y + 0.5, expanded_left, expanded_right))
            minimum = minimum.min(Vector2(expanded_left, y + 0.5))
            maximum = maximum.max(Vector2(expanded_right, y + 0.5))
        else:
            lefts.append(-1.0)
            rights.append(-1.0)
    if first_valid < 0 or maximum.y <= minimum.y:
        return {}
    # Extend the edge rather than returning a rectangle or a missing mask
    # above/below the pointed opening. Matching CPU/GPU curves are essential.
    for index in range(first_valid):
        lefts[index] = lefts[first_valid]
        rights[index] = rights[first_valid]
    for index in range(first_valid + 1, lefts.size()):
        if lefts[index] < 0.0:
            lefts[index] = lefts[index - 1]
            rights[index] = rights[index - 1]
    if make_contour:
        var raw_lefts := lefts.duplicate()
        var raw_rights := rights.duplicate()
        for index in range(lefts.size()):
            var left_window: Array[float] = []
            var right_window: Array[float] = []
            for neighbor in range(maxi(0, index - 2), mini(lefts.size(), index + 3)):
                left_window.append(raw_lefts[neighbor])
                right_window.append(raw_rights[neighbor])
            left_window.sort()
            right_window.sort()
            lefts[index] = left_window[left_window.size() >> 1]
            rights[index] = right_window[right_window.size() >> 1]
    var result := {"rows": rows, "lefts": lefts, "rights": rights, "bounds": Rect2(minimum, maximum - minimum), "size": size,
        "points": points, "alphas": alphas, "grid_indices": grid_indices, "grid_size": grid_size, "alpha_area": alpha_area}
    if make_contour:
        var contour := Image.create(1, lefts.size(), false, Image.FORMAT_RGF)
        for index in range(lefts.size()):
            contour.set_pixel(0, index, Color(lefts[index] / size.x, rights[index] / size.x, 0.0, 1.0))
        result["texture"] = ImageTexture.create_from_image(contour)
    return result
