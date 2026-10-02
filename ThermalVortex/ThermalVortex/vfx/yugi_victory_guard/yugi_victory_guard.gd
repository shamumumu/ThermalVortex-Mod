extends Node2D
## Host-owned time: raising at Attack; force seek(1, 0) on native lightning contact.
## No _exit_tree cleanup: Visuals are synchronously reparented to the summary.

const FILM_SHADER = preload("shield_film.gdshader")
const ACCENTS_SCRIPT = preload("shield_accents.gd")
const CANVAS := Vector2(386, 534)
const BODY_HEIGHT := 483.0
const STARTS := [0.0, 0.10, 0.22, 0.36, 0.51, 0.65, 0.79, 1.0]
const FEET := [Vector2(213.5, 522), Vector2(213.5, 521), Vector2(214, 518), Vector2(214, 518), Vector2(215, 524), Vector2(215.5, 530), Vector2(216, 530), Vector2(216.5, 532)]
const FINAL_PALM := Vector2(325, 27)
const FINGER_CLEARANCE := 18.0
# Upper hand silhouettes measured from original alpha, including the furthest tips.
const HAND_PROFILES := {
    5: [Vector2(298, 31), Vector2(302, 31), Vector2(310, 39), Vector2(318, 38), Vector2(322, 27), Vector2(326, 25), Vector2(334, 20), Vector2(342, 17), Vector2(346, 15), Vector2(351, 15)],
    6: [Vector2(298, 24), Vector2(302, 23), Vector2(310, 29), Vector2(318, 32), Vector2(326, 20), Vector2(334, 17), Vector2(338, 12), Vector2(346, 10), Vector2(350, 8), Vector2(355, 8)],
    7: [Vector2(302, 14), Vector2(306, 15), Vector2(314, 24), Vector2(322, 23), Vector2(326, 14), Vector2(330, 11), Vector2(338, 7), Vector2(342, 4), Vector2(350, 3), Vector2(357, 3)]
}
const HAND_PALMS := {5: Vector2(320, 47), 6: Vector2(323, 38), 7: FINAL_PALM}

var _frames: Array[Texture2D] = []
var _body: Sprite2D
var _film: Polygon2D
var _accents: Node2D
var _film_material: ShaderMaterial
var _foot := Vector2.ZERO
var _height := 0.0
var _body_scale := 1.0
var _facing := 1.0
var _contact_x := 0.0
var _center := Vector2.ZERO
var _radii := Vector2.ZERO
var _final_center := Vector2.ZERO
var _final_radii := Vector2.ZERO
var _palm := Vector2.ZERO
var _configured := false
var _progress := 0.0
var _impact_age := -1.0

func setup(frames: Array[Texture2D], config: Dictionary) -> bool:
    clear()
    if frames.size() != 8 or not (config.get("foot_origin") is Vector2):
        return false
    _height = float(config.get("target_height", 0.0))
    _foot = config["foot_origin"]
    if not is_finite(_height) or _height <= 0.0 or not _foot.is_finite():
        return false
    for frame in frames:
        if frame == null or frame.get_size() != CANVAS:
            return false
    _frames.assign(frames)
    _facing = -1.0 if float(config.get("facing_sign", 1.0)) < 0.0 else 1.0
    _body_scale = _height / BODY_HEIGHT
    _contact_x = _foot.x
    _palm = _foot + (FINAL_PALM - FEET[7]) * Vector2(_body_scale * _facing, _body_scale)
    # The completed canopy is centered over the actor, as in the supplied artwork.
    _final_radii = Vector2(_height * 0.68, _height * 0.62)
    _final_center.x = _foot.x
    _final_center.y = _center_limit_above_hand(_final_center.x, _final_radii, 7)
    _ensure_nodes()
    _configured = true
    seek(0.0, -1.0)
    return true

func seek(progress: float, impact_age: float = -1.0) -> void:
    if not _configured:
        return
    _progress = clampf(progress, 0.0, 1.0) if is_finite(progress) else 1.0
    _impact_age = maxf(-1.0, impact_age) if is_finite(impact_age) else -1.0
    var frame_index := 0
    for index in range(STARTS.size()):
        if _progress >= STARTS[index]:
            frame_index = index
    _body.texture = _frames[frame_index]
    _body.scale = Vector2(_body_scale * _facing, _body_scale)
    _body.position = _foot - FEET[frame_index] * _body.scale
    _body.show()
    var growth := smoothstep(0.70, 1.0, _progress)
    var hand_frame := maxi(5, frame_index)
    var hand_peak := _hand_peak(hand_frame)
    var seed := hand_peak - Vector2(0.0, FINGER_CLEARANCE * _body_scale)
    _palm = _source_point(HAND_PALMS[hand_frame], hand_frame)
    _center = seed.lerp(_final_center, growth)
    _radii = _final_radii * growth
    if growth > 0.001:
        # A growing dome begins above the fingers and never slices through them.
        _center.y = minf(_center.y, _center_limit_above_hand(_center.x, _radii, hand_frame))
    _film.visible = growth > 0.001
    _accents.visible = _film.visible
    if not _film.visible:
        return
    # Extra drawing room is only for soft outside glow and the dark underside;
    # the contact surface remains the exact upper ellipse in get_shield_geometry.
    _film.polygon = PackedVector2Array([_center + Vector2(-_radii.x * 1.04, -_radii.y * 1.04), _center + Vector2(_radii.x * 1.04, -_radii.y * 1.04), _center + Vector2(_radii.x * 1.04, _radii.y * 0.17), _center + Vector2(-_radii.x * 1.04, _radii.y * 0.17)])
    _film.uv = PackedVector2Array([Vector2(0, 0), Vector2(1, 0), Vector2(1, 1), Vector2(0, 1)])
    _film_material.set_shader_parameter("growth", growth)
    _film_material.set_shader_parameter("dome_center", _center)
    _film_material.set_shader_parameter("dome_radii", _radii)
    _film_material.set_shader_parameter("impact_age", _impact_age)
    var effect_time := _progress * 0.36 + maxf(0.0, _impact_age)
    _film_material.set_shader_parameter("effect_time", effect_time)
    var contact := get_contact_point()
    _film_material.set_shader_parameter("contact_uv", Vector2((contact.x - _center.x) / _radii.x, (contact.y - _center.y) / _radii.y))
    _accents.call("configure", _center, _radii, _palm, contact, _height, growth, _impact_age, effect_time)

func _source_point(point: Vector2, frame_index: int) -> Vector2:
    return _foot + (point - FEET[frame_index]) * Vector2(_body_scale * _facing, _body_scale)

func _hand_peak(frame_index: int) -> Vector2:
    var top := Vector2.ZERO
    top.y = INF
    for source: Vector2 in HAND_PROFILES[frame_index]:
        var point := _source_point(source, frame_index)
        if point.y < top.y:
            top = point
    return top

func _center_limit_above_hand(center_x: float, radii: Vector2, frame_index: int) -> float:
    var limit := INF
    if radii.x <= 0.0001:
        return limit
    for source: Vector2 in HAND_PROFILES[frame_index]:
        var point := _source_point(source, frame_index)
        var dx := (point.x - center_x) / radii.x
        if absf(dx) <= 1.0:
            var surface_offset := radii.y * sqrt(maxf(0.0, 1.0 - dx * dx))
            limit = minf(limit, point.y - FINGER_CLEARANCE * _body_scale + surface_offset)
    return limit

func get_shield_geometry() -> Dictionary:
    return {"center": _center, "radii": _radii}

func get_final_shield_geometry() -> Dictionary:
    return {"center": _final_center, "radii": _final_radii}

func get_final_surface_y(x: float) -> float:
    if _final_radii.x <= 0.0001 or _final_radii.y <= 0.0001:
        return INF
    var normalized := (x - _final_center.x) / _final_radii.x
    if absf(normalized) > 1.0:
        return INF
    return _final_center.y - _final_radii.y * sqrt(maxf(0.0, 1.0 - normalized * normalized))

func set_contact_x(x: float) -> void:
    if is_finite(x):
        _contact_x = x

func get_surface_y(x: float) -> float:
    if _radii.x <= 0.0001 or _radii.y <= 0.0001:
        return INF
    var normalized := (x - _center.x) / _radii.x
    if absf(normalized) > 1.0:
        return INF
    return _center.y - _radii.y * sqrt(maxf(0.0, 1.0 - normalized * normalized))

func get_contact_point() -> Vector2:
    var x := clampf(_contact_x, _center.x - _radii.x, _center.x + _radii.x)
    return Vector2(x, get_surface_y(x))

func get_frame_index() -> int:
    var result := 0
    for index in range(STARTS.size()):
        if _progress >= STARTS[index]:
            result = index
    return result

func clear() -> void:
    _configured = false
    _frames.clear()
    _radii = Vector2.ZERO
    if is_instance_valid(_body):
        _body.texture = null
        _body.hide()
    if is_instance_valid(_film):
        _film.hide()
    if is_instance_valid(_accents):
        _accents.hide()

func _ensure_nodes() -> void:
    if not is_instance_valid(_film):
        _film = Polygon2D.new()
        _film.name = "ShieldMembrane"
        _film.z_index = 0
        _film_material = ShaderMaterial.new()
        _film_material.shader = FILM_SHADER
        _film.material = _film_material
        add_child(_film)
    if not is_instance_valid(_body):
        _body = Sprite2D.new()
        _body.name = "Body"
        _body.z_index = 1
        _body.centered = false
        _body.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR
        add_child(_body)
    if not is_instance_valid(_accents):
        _accents = Node2D.new()
        _accents.name = "ShieldRimAndImpact"
        _accents.set_script(ACCENTS_SCRIPT)
        _accents.z_index = 2
        add_child(_accents)
