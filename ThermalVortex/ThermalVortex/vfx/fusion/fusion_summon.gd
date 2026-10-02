extends Control
## Shared production/preview scene. All visual state is a function of seek time.
## No gameplay state, audio lookup, global RNG, wall clock or game input is used.

signal finished

const INTRO_DURATION: float = 80.0 / 60.0
const MAIN_DURATION: float = 182.0 / 60.0
const DURATION: float = 262.0 / 60.0
const DATA_PATH: String = "res://ThermalVortex/vfx/fusion/source-data/curves.json"
const MASK: Texture2D = preload("res://ThermalVortex/vfx/fusion/textures/rounded_card_mask.png")
const FACE_FACTORY_SCRIPT: Script = preload("res://ThermalVortex/vfx/fusion/spire_card_faces.gd")
const CARD_ASPECT: float = 340.0 / 450.0
const NUM_INITIAL_OFFSET: Vector2 = Vector2(-0.74, 0.7726825)
const MESH_SHADER: Shader = preload("res://ThermalVortex/vfx/fusion/shaders/card_mesh.gdshader")
const WARP_SHADER: Shader = preload("res://ThermalVortex/vfx/fusion/shaders/card_warp.gdshader")
const ENERGY_SHADER: Shader = preload("res://ThermalVortex/vfx/fusion/shaders/fusion_energy.gdshader")
const PARTICLE_SCRIPT: Script = preload("res://ThermalVortex/vfx/fusion/fusion_particles.gd")

var _built: bool = false
var _configured: bool = false
var _playing: bool = false
var _paused: bool = true
var _completed: bool = false
var _time: float = 0.0
var _speed: float = 1.0
var _curves: Dictionary = {}
var _textures: Array[Texture2D] = []
var _mesh_cards: Array[MeshInstance3D] = []
var _mesh_materials: Array[ShaderMaterial] = []
var _warp_cards: Array[ColorRect] = []
var _warp_materials: Array[ShaderMaterial] = []
var _viewport: SubViewport
var _warp_viewport: SubViewport
var _face_factory: Node
var _back_texture: Texture2D
var _world: Node3D
var _camera: Camera3D
var _backdrop: ColorRect
var _energy: ColorRect
var _energy_material: ShaderMaterial
var _viewport_image: TextureRect
var _warp_parent: Control
var _warp_image: TextureRect
var _num_positions: Array[Vector2] = []
var _num_uv_scale: float = 2.0
var _particles: Node2D
var _result_mesh: MeshInstance3D
var _result_material: ShaderMaterial
var _show_result: bool = false
var _full_result_card: bool = false
var _dimensions: Vector2 = Vector2(1280.0, 720.0)
var _debug_curve: Dictionary = {}
var _configuration_id: int = 0
var _face_refresh_queued_for: int = -1
var _faces_ready: bool = false

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	process_mode = Node.PROCESS_MODE_ALWAYS
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(DATA_PATH))
	if parsed is Dictionary:
		_curves = parsed
	_build_nodes()
	_built = true
	resized.connect(_on_resized)
	_on_resized()
	visible = false
	set_process(false)

func _fill(control: Control) -> void:
	control.mouse_filter = Control.MOUSE_FILTER_IGNORE
	control.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

func _build_nodes() -> void:
	_face_factory = Node.new()
	_face_factory.name = "SpireCardFaceFactory"
	_face_factory.set_script(FACE_FACTORY_SCRIPT)
	add_child(_face_factory)
	_backdrop = ColorRect.new()
	_backdrop.name = "Backdrop"
	_backdrop.color = Color(0.012, 0.007, 0.027, 0.0)
	add_child(_backdrop)
	_fill(_backdrop)
	_energy = ColorRect.new()
	_energy.name = "RecoveredTexturesAndRebuiltEmitters"
	_energy_material = ShaderMaterial.new()
	_energy_material.shader = ENERGY_SHADER
	_energy_material.set_shader_parameter("swirl_009", preload("res://ThermalVortex/vfx/fusion/textures/fusion_swirl_009.png"))
	_energy_material.set_shader_parameter("swirl_010", preload("res://ThermalVortex/vfx/fusion/textures/fusion_swirl_010.png"))
	_energy_material.set_shader_parameter("swirl_011", preload("res://ThermalVortex/vfx/fusion/textures/fusion_swirl_011.png"))
	_energy_material.set_shader_parameter("swirl_012", preload("res://ThermalVortex/vfx/fusion/textures/fusion_swirl_012.png"))
	_energy_material.set_shader_parameter("material_uzu", preload("res://ThermalVortex/vfx/fusion/textures/material_whirlpool.png"))
	_energy_material.set_shader_parameter("gradient_mask", preload("res://ThermalVortex/vfx/fusion/textures/gradient_mask.png"))
	_energy.material = _energy_material
	add_child(_energy)
	_fill(_energy)

	_viewport = SubViewport.new()
	_viewport.name = "TransparentCardViewport"
	_viewport.transparent_bg = true
	_viewport.own_world_3d = true
	_viewport.size = Vector2i(1280, 720)
	_viewport.msaa_3d = Viewport.MSAA_4X
	_viewport.render_target_update_mode = SubViewport.UPDATE_ONCE
	add_child(_viewport)
	_world = Node3D.new()
	_world.name = "Cards"
	_viewport.add_child(_world)
	_camera = Camera3D.new()
	_camera.position = Vector3(0.0, 0.0, 12.0)
	_camera.fov = 40.0
	_camera.current = true
	_camera.near = 0.05
	_world.add_child(_camera)
	_viewport_image = TextureRect.new()
	_viewport_image.name = "PerspectiveCards"
	_viewport_image.texture = _viewport.get_texture()
	_viewport_image.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_viewport_image.stretch_mode = TextureRect.STRETCH_SCALE
	add_child(_viewport_image)
	_fill(_viewport_image)
	# Distortion draws once per material. Bound its transparent render target as
	# well as the 3D target, so thirty cards do not each shade a full 4K window.
	_warp_viewport = SubViewport.new()
	_warp_viewport.name = "TransparentWarpViewport"
	_warp_viewport.transparent_bg = true
	_warp_viewport.disable_3d = true
	_warp_viewport.size = Vector2i(1280, 720)
	_warp_viewport.render_target_update_mode = SubViewport.UPDATE_ONCE
	add_child(_warp_viewport)
	_warp_parent = Control.new()
	_warp_parent.name = "OriginalUVCardDistortion"
	_warp_viewport.add_child(_warp_parent)
	_fill(_warp_parent)
	_warp_image = TextureRect.new()
	_warp_image.name = "DistortedCards"
	_warp_image.texture = _warp_viewport.get_texture()
	_warp_image.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_warp_image.stretch_mode = TextureRect.STRETCH_SCALE
	add_child(_warp_image)
	_fill(_warp_image)
	_particles = Node2D.new()
	_particles.name = "ReconstructedDeterministicSparks"
	_particles.set_script(PARTICLE_SCRIPT)
	add_child(_particles)

func _on_resized() -> void:
	_dimensions = size if size.x > 2.0 and size.y > 2.0 else Vector2(1280.0, 720.0)
	if not _built:
		return
	# Preserve aspect without making a large display allocate an unbounded RT.
	var rt_scale: float = minf(1.0, 1600.0 / _dimensions.x)
	_viewport.size = Vector2i(maxi(16, roundi(_dimensions.x * rt_scale)), maxi(16, roundi(_dimensions.y * rt_scale)))
	_warp_viewport.size = _viewport.size
	_energy_material.set_shader_parameter("viewport_size", _dimensions)
	for material in _warp_materials:
		material.set_shader_parameter("viewport_size", _dimensions)
	if _configured:
		_apply_time()

func _clear_cards() -> void:
	for mesh in _mesh_cards:
		mesh.get_parent().remove_child(mesh)
		mesh.queue_free()
	for card in _warp_cards:
		card.get_parent().remove_child(card)
		card.queue_free()
	if is_instance_valid(_result_mesh):
		_result_mesh.get_parent().remove_child(_result_mesh)
		_result_mesh.queue_free()
	_result_mesh = null
	_result_material = null
	_mesh_cards.clear()
	_mesh_materials.clear()
	_warp_cards.clear()
	_warp_materials.clear()
	_textures.clear()
	_num_positions.clear()

func _is_full_card(texture: Texture2D) -> bool:
	return float(texture.get_width()) / maxf(1.0, float(texture.get_height())) < 0.90

func _make_mesh(texture: Texture2D) -> MeshInstance3D:
	var mesh := MeshInstance3D.new()
	var quad := QuadMesh.new()
	quad.size = Vector2(1.75, 1.75 / CARD_ASPECT)
	mesh.mesh = quad
	var material := ShaderMaterial.new()
	material.shader = MESH_SHADER
	material.set_shader_parameter("card_texture", texture)
	material.set_shader_parameter("back_texture", _back_texture)
	material.set_shader_parameter("rounded_mask", MASK)
	material.set_shader_parameter("full_card", _is_full_card(texture))
	mesh.material_override = material
	_world.add_child(mesh)
	return mesh

func configure(data: Dictionary) -> bool:
	if not _built or not is_inside_tree() or _curves.is_empty() or not bool(_face_factory.call("has_native_resources")):
		return false
	var materials_data = data.get("materials", [])
	if not materials_data is Array:
		return false
	_playing = false
	_paused = true
	_completed = false
	_configuration_id += 1
	_faces_ready = false
	_viewport_image.visible = false
	_warp_image.visible = false
	_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
	_warp_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
	_clear_cards()
	_face_factory.call("reset")
	_back_texture = _face_factory.call("get_face", null) as Texture2D
	for value in materials_data:
		var texture: Texture2D = _face_factory.call("get_face", value) as Texture2D
		if texture == null:
			texture = _back_texture
		_textures.append(texture)
		var mesh := _make_mesh(texture)
		mesh.name = "RealMaterial%d" % _textures.size()
		_mesh_cards.append(mesh)
		_mesh_materials.append(mesh.material_override as ShaderMaterial)
		var card := ColorRect.new()
		card.name = "WarpMaterial%d" % _textures.size()
		var material := ShaderMaterial.new()
		material.shader = WARP_SHADER
		material.set_shader_parameter("card_texture", texture)
		material.set_shader_parameter("rounded_mask", MASK)
		material.set_shader_parameter("full_card", _is_full_card(texture))
		material.set_shader_parameter("viewport_size", _dimensions)
		card.material = material
		_warp_parent.add_child(card)
		_fill(card)
		_warp_cards.append(card)
		_warp_materials.append(material)
	_prepare_num_layout(_textures.size())
	_show_result = bool(data.get("show_result", false))
	_full_result_card = false
	if _show_result:
		# A complete native result card already contains its name, cost and rules.
		# Keep it intact; the face factory intentionally omits text for materials.
		var result_texture: Texture2D = data.get("result_full_texture") as Texture2D
		_full_result_card = result_texture != null
		if result_texture == null:
			var result_value = data.get("result_card", data.get("result_texture"))
			result_texture = _face_factory.call("get_face", result_value) as Texture2D
		if result_texture == null:
			result_texture = _back_texture
		_result_mesh = _make_mesh(result_texture)
		_result_mesh.name = "OptionalPreviewResult"
		_result_material = _result_mesh.material_override as ShaderMaterial
	_configured = true
	_time = 0.0
	visible = true
	_apply_time()
	set_process(false)
	_queue_face_refresh()
	return true

func _queue_face_refresh() -> void:
	if _faces_ready or _face_refresh_queued_for == _configuration_id:
		return
	_face_refresh_queued_for = _configuration_id
	_refresh_after_faces_rendered.call_deferred(_configuration_id)

func _refresh_after_faces_rendered(configuration_id: int) -> void:
	# Shader sampler dependencies do not guarantee that the one-shot face RTs
	# finish before the card RTs. Keep consumers hidden until the first draw,
	# then refresh even when the authoring preview is paused at a static seek.
	await RenderingServer.frame_post_draw
	if configuration_id != _configuration_id or not is_inside_tree() or not _configured or not visible:
		return
	_faces_ready = true
	_viewport_image.visible = true
	_warp_image.visible = true
	_apply_time()

func _exit_tree() -> void:
	_configuration_id += 1
	_faces_ready = false

func get_duration() -> float:
	return DURATION

func play(speed: float = 1.0) -> void:
	if not _configured:
		return
	_speed = maxf(0.01, speed)
	_time = 0.0
	_completed = false
	_playing = true
	_paused = false
	visible = true
	_apply_time()
	set_process(true)
	_queue_face_refresh()

func seek(seconds: float) -> void:
	if not _configured:
		return
	_time = clampf(seconds, 0.0, DURATION)
	if _time < DURATION:
		_completed = false
	visible = true
	_apply_time()
	_queue_face_refresh()

func set_paused(paused: bool) -> void:
	_paused = paused
	if not paused and _configured and _time < DURATION:
		_playing = true
		_completed = false
	set_process(_configured and _playing and not _paused)

func stop() -> void:
	_configuration_id += 1
	_playing = false
	_paused = true
	_completed = false
	_time = 0.0
	set_process(false)
	if _configured:
		_apply_time()
	visible = false
	if is_instance_valid(_viewport):
		_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
	if is_instance_valid(_warp_viewport):
		_warp_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED

func _process(delta: float) -> void:
	if not _configured or not _playing or _paused:
		return
	_time = minf(DURATION, _time + maxf(0.0, delta) * _speed)
	_apply_time()
	if _time >= DURATION:
		_playing = false
		set_process(false)
		if not _completed:
			_completed = true
			finished.emit()

func get_debug_state() -> Dictionary:
	var state := {
		"time": _time, "main_time": _time - INTRO_DURATION,
		"material_count": _textures.size(), "playing": _playing and not _paused,
		"finished": _completed, "configured": _configured, "paused": _paused,
		"duration": DURATION, "show_result": _show_result,
		"full_result_card": _full_result_card,
		"visible_material_nodes": _warp_cards.size(),
		"mesh_material_nodes": _mesh_cards.size(),
		"warp_render_size": _warp_viewport.size,
		"num_layout": "grid" if _textures.size() > 12 else "ring",
		"num_uv_scale": _num_uv_scale,
	}
	state.merge(_debug_curve)
	return state

func _curve_clip(index: int) -> String:
	if _textures.size() > 5:
		return "summonfusionincard/Recorded (%d)" % (9 + index % 6)
	if _textures.size() == 5:
		return "summonfusionincard/Recorded (4)"
	return "summonfusionincard/Recorded"

func _curve_time(main_time: float) -> float:
	# Num's source animation lives inside a 2.0 s sub-playable beginning at 1/60.
	if _textures.size() > 5:
		return maxf(0.0, main_time - 1.0 / 60.0) * (2.0 / 3.0)
	return maxf(0.0, main_time)

func _evaluate(clip_name: String, attribute: String, seconds: float, default_value: float) -> float:
	var clip: Dictionary = _curves.get(clip_name, {})
	var float_curves: Dictionary = clip.get("float_curves", {})
	var curve: Dictionary = float_curves.get(attribute, {})
	var keys: Array = curve.get("keys", [])
	if keys.is_empty():
		return default_value
	if seconds <= float(keys[0]["time"]):
		return float(keys[0]["value"])
	for index in range(1, keys.size()):
		var right: Dictionary = keys[index]
		if seconds > float(right["time"]):
			continue
		var left: Dictionary = keys[index - 1]
		var dt: float = float(right["time"]) - float(left["time"])
		if dt <= 0.0:
			return float(right["value"])
		var u: float = (seconds - float(left["time"])) / dt
		var a: float = float(left["value"])
		var b: float = float(right["value"])
		var out_slope: float = float(left.get("outSlope", 0.0))
		var in_slope: float = float(right.get("inSlope", 0.0))
		var weighted_out: bool = (int(left.get("weightedMode", 0)) & 2) != 0
		var weighted_in: bool = (int(right.get("weightedMode", 0)) & 1) != 0
		if weighted_out or weighted_in:
			var ow: float = float(left.get("outWeight", 1.0 / 3.0)) if weighted_out else 1.0 / 3.0
			var iw: float = float(right.get("inWeight", 1.0 / 3.0)) if weighted_in else 1.0 / 3.0
			var low: float = 0.0
			var high: float = 1.0
			for iteration in range(18):
				var mid: float = (low + high) * 0.5
				var bx: float = _bezier(0.0, ow, 1.0 - iw, 1.0, mid)
				if bx < u:
					low = mid
				else:
					high = mid
			return _bezier(a, a + out_slope * dt * ow, b - in_slope * dt * iw, b, (low + high) * 0.5)
		var u2: float = u * u
		var u3: float = u2 * u
		return (2.0 * u3 - 3.0 * u2 + 1.0) * a + (u3 - 2.0 * u2 + u) * dt * out_slope + (-2.0 * u3 + 3.0 * u2) * b + (u3 - u2) * dt * in_slope
	return float(keys[keys.size() - 1]["value"])

func _bezier(a: float, b: float, c: float, d: float, t: float) -> float:
	var v: float = 1.0 - t
	return v * v * v * a + 3.0 * v * v * t * b + 3.0 * v * t * t * c + t * t * t * d

func _intro_position(index: int, count: int) -> Vector3:
	if count == 1:
		return Vector3.ZERO
	if count <= 3:
		return Vector3((float(index) - float(count - 1) * 0.5) * 2.15, 0.15 - absf(float(index) - float(count - 1) * 0.5) * 0.20, 0.0)
	if count > 12:
		var num_columns: int = maxi(1, roundi(sqrt(float(count) * 1.2)))
		var num_scale: float = minf(1.0, 4.0 / float(num_columns))
		# Use the same row/column order in both phases, including a centred
		# incomplete last row, rather than moving its final cards to the left.
		var point: Vector2 = _num_positions[index] / 1.05
		return Vector3(point.x * 2.05 * num_scale, -point.y * 2.55 * num_scale, -0.1 * absf(point.x))
	var columns: int = ceili(sqrt(float(count) * 1.6))
	var rows: int = ceili(float(count) / float(columns))
	var column: int = index % columns
	var row: int = index / columns
	var scale_factor: float = minf(1.0, 4.0 / float(columns))
	return Vector3((float(column) - float(columns - 1) * 0.5) * 2.05 * scale_factor, (float(rows - 1) * 0.5 - float(row)) * 2.55 * scale_factor, -0.1 * absf(float(column) - float(columns - 1) * 0.5))

func _layout_offset(index: int, count: int) -> Vector2:
	if count <= 1:
		return Vector2(-0.5, -0.5)
	if count == 2:
		return Vector2(0.1 if index == 0 else -1.1, -0.5)
	if count == 3:
		return [Vector2(-0.5, -1.0), Vector2(0.2, 0.0), Vector2(-1.2, 0.0)][index]
	if count == 4:
		return [Vector2(0.1, -1.05), Vector2(0.1, 0.05), Vector2(-1.1, 0.05), Vector2(-1.1, -1.05)][index]
	if count == 5:
		return [Vector2(0.0, -0.87), Vector2(1.35, -0.25), Vector2(0.9, 0.87), Vector2(-0.9, 0.87), Vector2(-1.3, -0.25)][index] - Vector2(0.875, 0.875)
	return Vector2.ZERO # Num uses _num_layout_offset with the recovered curves.

func _prepare_num_layout(count: int) -> void:
	_num_positions.clear()
	_num_uv_scale = 2.0 * maxf(1.0, sqrt(float(count) / 6.0))
	if count <= 12:
		return
	# Twenty/thirty materials use a five-by-four/six-by-five array. Every slot
	# remains independent; its recovered Num curve set still follows i % 6.
	var columns: int = maxi(1, roundi(sqrt(float(count) * 1.2)))
	var rows: int = ceili(float(count) / float(columns))
	var radius: float = 0.0
	for index in range(count):
		var row: int = index / columns
		var row_count: int = mini(columns, count - row * columns)
		var point := Vector2(float(index % columns) - float(row_count - 1) * 0.5, float(row) - float(rows - 1) * 0.5) * 1.05
		_num_positions.append(point)
		radius = maxf(radius, point.length())
	# Leave space for a full card corner while the original rotation turns the
	# whole arrangement. This avoids clipping on tall and wide desktop displays.
	_num_uv_scale = maxf(_num_uv_scale, (radius + 0.75) * 1.6)

func _num_layout_offset(index: int, count: int, original_offset: Vector2) -> Vector2:
	var layout: Vector2
	if count <= 12:
		layout = original_offset.rotated(TAU * float(index) / float(count))
	else:
		var radial_motion: float = original_offset.length() / NUM_INITIAL_OFFSET.length()
		var angular_motion: float = original_offset.angle() - NUM_INITIAL_OFFSET.angle()
		layout = _num_positions[index].rotated(angular_motion) * radial_motion
	# card_uv = warped_uv * S + offset: its centre must remain 0.5 when S
	# changes with material count. The old fixed -0.5 only centred S == 2.
	return layout + Vector2.ONE * (1.0 - _num_uv_scale) * 0.5

func _apply_time() -> void:
	var mt: float = _time - INTRO_DURATION
	var intro: float = clampf(_time, 0.0, INTRO_DURATION)
	var opening: float = smoothstep(0.0, 0.18, _time)
	var closing: float = 1.0 - smoothstep(DURATION - 0.26, DURATION, _time)
	_backdrop.color = Color(0.014, 0.009, 0.030, 0.70 * opening * closing)
	_energy_material.set_shader_parameter("intro_time", intro)
	_energy_material.set_shader_parameter("intro_mask_offset", _evaluate("summonfusionshowunitcard/Recorded", "_offset", maxf(0.0, intro - 1.0 / 3.0), 0.0))
	_energy_material.set_shader_parameter("intro_amplitude", _evaluate("summonfusionshowunitcard/Recorded", "_Amplitude", maxf(0.0, intro - 1.0 / 3.0), 1.0))
	_energy_material.set_shader_parameter("main_time", mt)
	_energy_material.set_shader_parameter("global_alpha", opening * closing)
	var count: int = _textures.size()
	var intro_scale: float = 1.22 if count <= 3 else minf(0.89, 3.50 / maxf(4.0, sqrt(float(count) * 1.6)))
	for i in range(count):
		var mesh: MeshInstance3D = _mesh_cards[i]
		var material: ShaderMaterial = _mesh_materials[i]
		mesh.visible = mt < 0.0 and _time > 0.0
		var entrance: float = smoothstep(0.02 + float(i) * minf(0.035, 0.14 / maxf(1.0, float(count))), 0.42, intro)
		var departure: float = smoothstep(1.19, INTRO_DURATION, intro)
		var position_target: Vector3 = _intro_position(i, count)
		mesh.position = position_target.lerp(Vector3(0.0, 0.0, -3.5), departure)
		mesh.position.y += (1.0 - entrance) * 1.7
		mesh.rotation = Vector3(deg_to_rad(lerpf(12.0, 0.0, entrance)), lerpf(PI, -0.10 * signf(position_target.x), entrance), deg_to_rad(position_target.x * -1.7 + sin(intro * 2.0 + float(i)) * 1.2))
		mesh.scale = Vector3.ONE * intro_scale * lerpf(0.84, 1.0, entrance) * lerpf(1.0, 0.57, departure)
		material.set_shader_parameter("opacity", entrance * (1.0 - departure))
		material.set_shader_parameter("edge_light", 0.2 + 0.42 * sin(intro * PI / INTRO_DURATION))
		material.set_shader_parameter("rim_color", Color(0.95, 0.11, 0.24) if i % 2 == 0 else Color(0.20, 0.31, 1.0))

		var clip_name: String = _curve_clip(i)
		var ct: float = _curve_time(mt)
		var rotation_value: float = _evaluate(clip_name, "_Rotate", ct, 0.0)
		var twist_value: float = _evaluate(clip_name, "_TwistStrength", ct, 0.0)
		var scale_value: float = _evaluate(clip_name, "_Scale", ct, 1.28)
		if i == 0:
			_debug_curve = {"curve_clip": clip_name, "curve_time": ct, "curve_rotate": rotation_value, "curve_twist": twist_value, "curve_scale": scale_value}
		var warp_material: ShaderMaterial = _warp_materials[i]
		var stage_end: float = 2.0166667 if count > 5 else (1.3833333 if count == 3 else INTRO_DURATION)
		_warp_cards[i].visible = mt >= 0.0 and mt <= stage_end
		warp_material.set_shader_parameter("scale_factor", scale_value)
		warp_material.set_shader_parameter("rotate_radians", rotation_value)
		warp_material.set_shader_parameter("twist_strength", twist_value)
		warp_material.set_shader_parameter("plane_size", 1.08 if count == 5 else 1.02)
		var layout_offset: Vector2 = _layout_offset(i, count)
		if count > 5:
			var original_offset := Vector2(_evaluate(clip_name, "_CardAOffset.x", ct, -0.74), _evaluate(clip_name, "_CardAOffset.y", ct, 0.7726825))
			layout_offset = _num_layout_offset(i, count, original_offset)
		warp_material.set_shader_parameter("card_offset", layout_offset)
		var uv_scale: float = 2.75 if count == 5 else 2.0
		if count > 5:
			uv_scale = _num_uv_scale
		warp_material.set_shader_parameter("card_uv_scale", Vector2.ONE * uv_scale)
		var entry_motion: float = smoothstep(0.0, 0.4666667, ct) if count <= 4 else 1.0
		# Compress original world-unit travel to the overlay's safe screen area.
		# This changes screen framing, never the recovered material curves.
		warp_material.set_shader_parameter("plane_center", Vector2(0.5, 0.5 - (1.0 - entry_motion) * 0.10))
		var tint: Color = Color(0.5, 0.5, 0.5, 1.0)
		if count <= 4:
			var suffix: String = "A" if i % 2 == 0 else "B"
			tint = Color(_evaluate(clip_name, "_Color" + suffix + ".r", ct, 0.5), _evaluate(clip_name, "_Color" + suffix + ".g", ct, 0.5), _evaluate(clip_name, "_Color" + suffix + ".b", ct, 0.5), _evaluate(clip_name, "_Color" + suffix + ".a", ct, 1.0))
		else:
			tint = Color(_evaluate(clip_name, "_ColorA.r", ct, 0.5), _evaluate(clip_name, "_ColorA.g", ct, 0.5), _evaluate(clip_name, "_ColorA.b", ct, 0.5), _evaluate(clip_name, "_ColorA.a", ct, 1.0))
		warp_material.set_shader_parameter("card_color", tint)
		warp_material.set_shader_parameter("opacity", (1.0 - smoothstep(stage_end - 0.13, stage_end, mt)) * closing)
	if count == 0:
		_debug_curve = {"curve_clip": "none", "curve_time": 0.0, "curve_rotate": 0.0, "curve_twist": 0.0, "curve_scale": 1.0}
	if is_instance_valid(_result_mesh):
		var reveal: float = smoothstep(1.83, 2.18, mt)
		_result_mesh.visible = mt >= 1.83 and mt < MAIN_DURATION
		_result_mesh.position = Vector3(0.0, 0.0, lerpf(-3.0, 0.8, reveal))
		_result_mesh.rotation = Vector3(0.0, lerpf(-0.5, 0.0, reveal), 0.0)
		_result_mesh.scale = Vector3.ONE * lerpf(0.65, 1.48, reveal)
		_result_material.set_shader_parameter("opacity", reveal * closing)
		_result_material.set_shader_parameter("edge_light", 0.68 * reveal * closing)
	_particles.call("sample", mt, _dimensions)
	var update_mode: int = SubViewport.UPDATE_ONCE if _faces_ready else SubViewport.UPDATE_DISABLED
	_viewport.render_target_update_mode = update_mode
	_warp_viewport.render_target_update_mode = update_mode
