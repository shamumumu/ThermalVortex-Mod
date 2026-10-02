extends Node
## Native Spire presentation with every text label and the energy UI omitted.
## This is a visual composition, not a live NCard or CardModel. Every unique
## texture/frame combination renders once; duplicate materials share its RT.
## The caller releases displayed textures before reset; freeing us frees all RTs.

const FACE_SIZE: Vector2i = Vector2i(340, 450)
const CENTER: Vector2 = Vector2(170.0, 226.0)
const FRAME_SHADER: Shader = preload("res://ThermalVortex/vfx/fusion/spire-card/art_frame.gdshader")
const NATIVE_CARD_ATLAS: String = "res://images/atlases/ui_atlas.sprites/card/"

var BANNER_SHADER: Shader
var FRAMES: Dictionary = {}
var BORDERS: Dictionary = {}
var BANNER: Texture2D
var PLAQUE: Texture2D
const RARITY_TINTS: Dictionary = {
	"basic": Vector3(1.0, 0.0, 0.85), "common": Vector3(1.0, 0.0, 0.85), "token": Vector3(1.0, 0.0, 0.85),
	"uncommon": Vector3(1.0, 1.0, 1.0), "rare": Vector3(0.563, 1.198, 1.14),
	"event": Vector3(0.875, 0.85, 0.9), "special": Vector3(0.875, 0.85, 0.9),
	"curse": Vector3(0.27, 1.1, 0.9), "status": Vector3(0.634, 0.35, 0.8), "quest": Vector3(0.515, 1.727, 0.9),
}

var _faces: Dictionary = {}
var _viewports: Array[SubViewport] = []

func _init() -> void:
	# The game mounts its own resources before this scene is instantiated.
	# Dynamic loads keep the standalone mod importer independent of the game PCK.
	var atlas_images: Dictionary = {}
	BANNER_SHADER = ResourceLoader.load("res://shaders/hsv.gdshader") as Shader
	for card_type in ["attack", "skill", "power"]:
		FRAMES[card_type] = _load_native_texture(NATIVE_CARD_ATLAS + "card_frame_%s_s.tres" % card_type, atlas_images)
		BORDERS[card_type] = _load_native_texture(NATIVE_CARD_ATLAS + "card_portrait_border_%s_s.tres" % card_type, atlas_images)
	BANNER = _load_native_texture(NATIVE_CARD_ATLAS + "card_banner.tres", atlas_images)
	PLAQUE = _load_native_texture("res://images/ui/cards/card_portrait_border_plaque2.png", atlas_images)

func has_native_resources() -> bool:
	if BANNER_SHADER == null or BANNER == null or PLAQUE == null:
		return false
	for card_type in ["attack", "skill", "power"]:
		if FRAMES.get(card_type) == null or BORDERS.get(card_type) == null:
			return false
	return true

func _load_native_texture(path: String, atlas_images: Dictionary) -> Texture2D:
	var source: Texture2D = ResourceLoader.load(path) as Texture2D
	if source == null:
		push_error("Cannot load native fusion card texture: " + path)
		return null
	var image: Image
	if source is AtlasTexture:
		# Flatten the atlas in memory: the frame shader needs UVs local to the card.
		# Preserve native transparent margins, including the banner's top padding.
		var atlas: AtlasTexture = source as AtlasTexture
		var atlas_image: Image = atlas_images.get(atlas.atlas)
		if atlas_image == null:
			atlas_image = atlas.atlas.get_image()
			if atlas_image.is_compressed():
				atlas_image.decompress()
			atlas_image.convert(Image.FORMAT_RGBA8)
			atlas_images[atlas.atlas] = atlas_image
		image = Image.create(source.get_width(), source.get_height(), false, Image.FORMAT_RGBA8)
		image.blit_rect(atlas_image, Rect2i(atlas.region), Vector2i(atlas.margin.position))
	else:
		image = source.get_image()
	return ImageTexture.create_from_image(image)

func reset() -> void:
	_faces.clear()
	for viewport in _viewports:
		if is_instance_valid(viewport):
			viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
			if viewport.get_parent() == self:
				remove_child(viewport)
			viewport.queue_free()
	_viewports.clear()

func get_face(entry: Variant) -> Texture2D:
	if not has_native_resources():
		return null
	var data: Dictionary = {}
	var is_back: bool = entry == null
	if entry is Dictionary:
		data = entry
	elif entry is Texture2D:
		data = {"portrait": entry}
	else:
		is_back = true
	var portrait: Texture2D = data.get("portrait") as Texture2D
	var card_type: String = str(data.get("type", "skill")).to_lower()
	var frame_type: String = card_type if FRAMES.has(card_type) else "skill"
	var extra_deck: bool = bool(data.get("extra_deck", false))
	var frame_hsv: Vector3 = Vector3(float(data.get("frame_h", 0.756)), float(data.get("frame_s", 0.77)), float(data.get("frame_v", 0.75)))
	var rarity: String = str(data.get("rarity", "common")).to_lower()
	var default_banner: Vector3 = RARITY_TINTS.get(rarity, RARITY_TINTS["common"])
	var banner_hsv: Vector3 = Vector3(float(data.get("banner_h", default_banner.x)), float(data.get("banner_s", default_banner.y)), float(data.get("banner_v", default_banner.z)))
	# The complete serialized key avoids collisions between different card faces.
	var key: Array = [is_back, portrait.get_instance_id() if portrait != null else 0,
		frame_type, extra_deck, frame_hsv.x, frame_hsv.y, frame_hsv.z,
		rarity, banner_hsv.x, banner_hsv.y, banner_hsv.z]
	var cache_key: String = JSON.stringify(key)
	if _faces.has(cache_key):
		return _faces[cache_key]
	var viewport: SubViewport = SubViewport.new()
	viewport.name = "SpireCardFace%d" % _viewports.size()
	viewport.size = FACE_SIZE
	viewport.disable_3d = true
	viewport.transparent_bg = true
	viewport.gui_disable_input = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ONCE
	viewport.render_target_clear_mode = SubViewport.CLEAR_MODE_ALWAYS
	add_child(viewport)
	_viewports.append(viewport)
	var material: ShaderMaterial = ShaderMaterial.new()
	material.shader = FRAME_SHADER
	material.set_shader_parameter("portrait", portrait if portrait != null else FRAMES[frame_type])
	material.set_shader_parameter("has_portrait", portrait != null)
	material.set_shader_parameter("card_back", is_back or portrait == null)
	material.set_shader_parameter("extra_deck", extra_deck)
	material.set_shader_parameter("frame_hsv", frame_hsv)
	material.set_shader_parameter("portrait_size", Vector2(portrait.get_size()) if portrait != null else Vector2.ONE)
	var shadow: TextureRect = TextureRect.new()
	shadow.name = "Shadow"
	shadow.position = CENTER + Vector2(-138.0, -199.0)
	shadow.size = Vector2(300.0, 422.0)
	shadow.texture = FRAMES[frame_type]
	shadow.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	shadow.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	shadow.modulate = Color(0.0, 0.0, 0.0, 0.25098)
	shadow.mouse_filter = Control.MOUSE_FILTER_IGNORE
	viewport.add_child(shadow)
	var frame: TextureRect = TextureRect.new()
	frame.name = "ArtAndNativeFrame"
	frame.position = CENTER + Vector2(-150.0, -211.0)
	frame.size = Vector2(300.0, 422.0)
	frame.texture = FRAMES[frame_type]
	frame.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	frame.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	frame.mouse_filter = Control.MOUSE_FILTER_IGNORE
	frame.material = material
	viewport.add_child(frame)
	if not is_back:
		_add_native_overlays(viewport, frame_type, banner_hsv)
	var face: Texture2D = viewport.get_texture()
	_faces[cache_key] = face
	return face

func get_debug_state() -> Dictionary:
	return {"unique_faces": _faces.size(), "viewports": _viewports.size(), "width": FACE_SIZE.x, "height": FACE_SIZE.y}

func _image(parent: Node, image: Texture2D, rect: Rect2, material: ShaderMaterial, stretch: int = TextureRect.STRETCH_KEEP_ASPECT_CENTERED) -> void:
	var node: TextureRect = TextureRect.new()
	# Ignore the source PNG's minimum size before assigning its native UI rect.
	# Otherwise a 551px source border clamps the intended 275px Control width.
	node.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	node.texture = image
	node.material = material
	node.stretch_mode = stretch
	node.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(node)
	node.position = CENTER + rect.position
	node.size = rect.size

func _add_native_overlays(viewport: SubViewport, frame_type: String, banner_hsv: Vector3) -> void:
	var banner_material: ShaderMaterial = ShaderMaterial.new()
	banner_material.shader = BANNER_SHADER
	banner_material.set_shader_parameter("h", banner_hsv.x)
	banner_material.set_shader_parameter("s", banner_hsv.y)
	banner_material.set_shader_parameter("v", banner_hsv.z)
	# Native card.tscn stacking and relative rectangles. The center moves up four
	# pixels so the native shadow also fits completely in the 340 x 450 canvas.
	_image(viewport, BORDERS[frame_type], Rect2(-137.5, -164.0, 275.0, 210.0), banner_material)
	_image(viewport, BANNER, Rect2(-163.0, -207.0, 327.0, 83.0), banner_material, TextureRect.STRETCH_KEEP_ASPECT_COVERED)
	var plaque: NinePatchRect = NinePatchRect.new()
	plaque.name = "TypePlaque"
	plaque.position = CENTER + Vector2(-30.5, 1.0)
	plaque.size = Vector2(61.0, 37.0)
	plaque.texture = PLAQUE
	plaque.material = banner_material
	plaque.patch_margin_left = 13
	plaque.patch_margin_right = 12
	plaque.mouse_filter = Control.MOUSE_FILTER_IGNORE
	viewport.add_child(plaque)
