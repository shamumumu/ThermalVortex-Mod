extends Node2D
## Reconstructed deterministic emitters; original emitter components were absent.

var sampled_time: float = -1.0
var canvas_size: Vector2 = Vector2(1280.0, 720.0)

func sample(seconds: float, dimensions: Vector2) -> void:
	sampled_time = seconds
	canvas_size = dimensions
	queue_redraw()

func _draw() -> void:
	if sampled_time < 0.0 or sampled_time > 3.0333334:
		return
	var center := canvas_size * 0.5
	var unit := canvas_size.y
	var t := sampled_time
	var tail := 1.0 - smoothstep(2.3, 3.0333334, t)
	for i in range(36):
		var seed_value := float(i) * 0.61803398875
		var angle := float(i) * 2.39996323 + t * (0.42 + fmod(seed_value, 0.6))
		var radial := 0.08 + fmod(seed_value * 0.47, 0.33)
		var pulse := 0.35 + 0.65 * pow(sin(float(i) * 1.7 + t * 2.2), 2.0)
		var envelope := smoothstep(0.2, 0.8, t) * tail
		if t < 1.8:
			radial *= lerpf(1.0, 0.15, smoothstep(0.8, 1.8, t))
		else:
			radial = 0.03 + radial * minf((t - 1.8) * 1.35, 1.0)
		var direction := Vector2(cos(angle), sin(angle))
		var location := center + direction * radial * unit
		var color := Color(1.0, 0.16, 0.31, envelope * pulse * 0.68) if i % 2 == 0 else Color(0.27, 0.40, 1.0, envelope * pulse * 0.68)
		var length_px := (2.5 + fmod(seed_value, 1.0) * 7.0) * unit / 720.0
		draw_line(location, location - direction * length_px, color, 1.1, true)
		draw_circle(location, 1.15 * unit / 720.0, color)
