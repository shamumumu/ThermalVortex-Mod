extends SceneTree
## Packs the already-imported staging project without exporting a game or
## requiring export templates. Original files stay available for direct reads.


func _collect_files(resource_directory: String, files: Array[String]) -> Error:
	var directory := DirAccess.open(resource_directory)
	if directory == null:
		return DirAccess.get_open_error()
	directory.include_hidden = true
	directory.include_navigational = false
	var result: Error = directory.list_dir_begin()
	if result != OK:
		return result
	var entry := directory.get_next()
	while not entry.is_empty():
		var resource_path := resource_directory.path_join(entry)
		if directory.current_is_dir():
			result = _collect_files(resource_path, files)
			if result != OK:
				directory.list_dir_end()
				return result
		else:
			files.append(resource_path)
		entry = directory.get_next()
	directory.list_dir_end()
	return OK


func _check_texture_imports(files: Array[String]) -> bool:
	for resource_path in files:
		if not ["png", "jpg", "jpeg"].has(resource_path.get_extension().to_lower()):
			continue
		var import_path := resource_path + ".import"
		var import_config := ConfigFile.new()
		var result: Error = import_config.load(import_path)
		if result != OK:
			push_error("Missing or invalid texture import remap: %s (error %d)" % [import_path, result])
			return false
		var source_file: String = import_config.get_value("deps", "source_file", "")
		var destinations: PackedStringArray = import_config.get_value("deps", "dest_files", PackedStringArray())
		if source_file != resource_path or destinations.is_empty():
			push_error("Incomplete texture import mapping: " + import_path)
			return false
		for destination in destinations:
			if not destination.begins_with("res://.godot/imported/") or not FileAccess.file_exists(destination):
				push_error("Missing texture import destination: " + destination)
				return false
	return true


func _initialize() -> void:
	var arguments := OS.get_cmdline_user_args()
	if arguments.size() != 1:
		push_error("Usage: pack-native-resources.gd -- <output.pck>")
		quit(2)
		return

	var files: Array[String] = []
	for resource_directory in ["res://ThermalVortex", "res://.godot/imported"]:
		var collect_result: Error = _collect_files(resource_directory, files)
		if collect_result != OK:
			push_error("Cannot enumerate %s (error %d)" % [resource_directory, collect_result])
			quit(3)
			return
	if not _check_texture_imports(files):
		quit(4)
		return
	files.sort()

	var packer := PCKPacker.new()
	var result: Error = packer.pck_start(arguments[0])
	if result != OK:
		push_error("Cannot create PCK %s (error %d)" % [arguments[0], result])
		quit(5)
		return
	for resource_path in files:
		result = packer.add_file(resource_path, ProjectSettings.globalize_path(resource_path))
		if result != OK:
			push_error("Cannot add %s to PCK (error %d)" % [resource_path, result])
			quit(6)
			return
	result = packer.flush()
	if result != OK:
		push_error("Cannot finish PCK (error %d)" % result)
		quit(7)
		return
	print("Packed %d native resource files to %s" % [files.size(), arguments[0]])
	quit(0)
