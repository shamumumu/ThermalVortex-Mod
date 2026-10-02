extends Node

signal impacted

const CLIP_SHADER = preload("clip.gdshader")
const ANIMATION_NAME = &"vfx_attack_lightning"
var contact_time := -1.0
var _guard: Node2D
var _lightning: Node2D
var _sprite: Sprite2D
var _player: AnimationPlayer
var _material: ShaderMaterial
var _hit := false

# The original scene, textures, positions and key times are retained. Only this
# instance's animation library and sprite material are replaced.
func setup(lightning: Node2D, guard: Node2D) -> bool:
    _guard = guard
    _lightning = lightning
    _sprite = lightning.get_node_or_null("Animation") as Sprite2D
    _player = lightning.get_node_or_null("AnimationPlayer") as AnimationPlayer
    if _sprite == null or _player == null or not _player.has_animation(ANIMATION_NAME):
        return false
    if _player.get_node(_player.root_node) != lightning:
        return false
    var animation := _player.get_animation(ANIMATION_NAME).duplicate(true) as Animation
    contact_time = _find_contact_time(animation)
    if contact_time < 0.0:
        return false
    var contact_track := animation.add_track(Animation.TYPE_METHOD)
    animation.track_set_path(contact_track, NodePath(str(lightning.get_path_to(self))))
    animation.track_insert_key(contact_track, contact_time, {"method": &"_on_contact", "args": []})
    var library := AnimationLibrary.new()
    library.add_animation(ANIMATION_NAME, animation)
    _player.stop()
    for library_name in _player.get_animation_library_list():
        _player.remove_animation_library(library_name)
    _player.add_animation_library(&"", library)
    _player.callback_mode_method = AnimationMixer.ANIMATION_CALLBACK_MODE_METHOD_IMMEDIATE
    _material = ShaderMaterial.new()
    _material.shader = CLIP_SHADER
    _sprite.material = _material
    process_priority = 10
    _player.play(ANIMATION_NAME)
    _player.advance(0.0)
    update_clip()
    if is_zero_approx(contact_time):
        _on_contact()
    return true

func _find_contact_time(animation: Animation) -> float:
    var texture_track := animation.find_track(NodePath("Animation:texture"), Animation.TYPE_VALUE)
    var position_track := animation.find_track(NodePath("Animation:position"), Animation.TYPE_VALUE)
    if texture_track < 0 or position_track < 0:
        return -1.0
    var transform := _guard.global_transform.affine_inverse() * _lightning.global_transform
    for key in animation.track_get_key_count(texture_track):
        var time := animation.track_get_key_time(texture_track, key)
        var texture := animation.track_get_key_value(texture_track, key) as Texture2D
        if texture == null:
            return -1.0
        var pos: Vector2 = animation.value_track_interpolate(position_track, time)
        var image := texture.get_image()
        if image == null or image.is_empty():
            return -1.0
        # The native ray bends substantially. Find its opaque crossing of the
        # curved dome, rather than placing the flash at the scene-root x.
        var local_transform := transform * Transform2D(_sprite.rotation, _sprite.scale, _sprite.skew, pos)
        var inverse := local_transform.affine_inverse()
        var half_size := texture.get_size() * 0.5
        var weighted_x := 0.0
        var total_weight := 0.0
        for x in range(0, image.get_width(), 2):
            var guard_x := (local_transform * Vector2(x - half_size.x, 0.0)).x
            var surface: float = _guard.call("get_final_surface_y", guard_x)
            if not is_finite(surface):
                continue
            var pixel := inverse * Vector2(guard_x, surface) + half_size
            var px := roundi(pixel.x)
            if px < 0 or px >= image.get_width():
                continue
            for row in range(-2, 3):
                var py := roundi(pixel.y) + row
                if py < 0 or py >= image.get_height():
                    continue
                var color := image.get_pixel(px, py)
                if color.a >= 0.3:
                    var weight := color.a * maxf(color.r, maxf(color.g, color.b))
                    weighted_x += guard_x * weight
                    total_weight += weight
        if total_weight > 0.01:
            _guard.call("set_contact_x", weighted_x / total_weight)
            return time
    return -1.0

func _on_contact() -> void:
    if _hit or not is_instance_valid(_guard) or not _guard.is_inside_tree():
        return
    _hit = true
    # Immediate method callback runs on the same native AnimationPlayer update
    # that switches the descending ray to its first contacting texture.
    _guard.call("seek", 1.0, 0.0)
    update_clip()
    impacted.emit()

func update_clip() -> void:
    if not is_instance_valid(_guard) or not is_instance_valid(_sprite) or _material == null:
        return
    var geometry: Dictionary = _guard.call("get_shield_geometry")
    var transform := _guard.global_transform.affine_inverse() * _sprite.global_transform
    _material.set_shader_parameter("guard_origin", transform.origin)
    _material.set_shader_parameter("guard_x", transform.x)
    _material.set_shader_parameter("guard_y", transform.y)
    _material.set_shader_parameter("dome_center", geometry["center"])
    _material.set_shader_parameter("dome_radii", geometry["radii"])

func _process(_delta: float) -> void:
    if not is_instance_valid(_guard) or not _guard.is_inside_tree():
        if is_instance_valid(_lightning):
            _lightning.queue_free()
        return
    update_clip()
