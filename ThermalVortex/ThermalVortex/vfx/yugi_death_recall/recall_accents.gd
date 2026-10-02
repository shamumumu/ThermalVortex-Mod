extends Node2D
## Local, deterministic accents driven by the same seek clock as the actor.
## Coordinates are in actor heights; the owner supplies placement and facing.

const DURATION := 2.1
const PARTICLE_COUNT := 34
const VIOLET := Color(0.64, 0.28, 1.0)
const GOLD := Color(1.0, 0.72, 0.30)

var _time := DURATION
var _glow: GradientTexture2D


func _init() -> void:
    var gradient := Gradient.new()
    gradient.offsets = PackedFloat32Array([0.0, 0.22, 0.58, 1.0])
    gradient.colors = PackedColorArray([
        Color(1.0, 1.0, 1.0, 0.9), Color(1.0, 1.0, 1.0, 0.52),
        Color(1.0, 1.0, 1.0, 0.10), Color(1.0, 1.0, 1.0, 0.0)])
    _glow = GradientTexture2D.new()
    _glow.gradient = gradient
    _glow.width = 64
    _glow.height = 64
    _glow.fill = GradientTexture2D.FILL_RADIAL
    _glow.fill_from = Vector2(0.5, 0.5)
    _glow.fill_to = Vector2(1.0, 0.5)
    texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR


func seek(time: float) -> void:
    _time = time
    visible = time > 0.0 and time < DURATION
    queue_redraw()


func clear() -> void:
    _time = DURATION
    visible = false
    queue_redraw()


func get_flash_strength() -> float:
    return smoothstep(1.985, 2.025, _time) * (1.0 - smoothstep(2.035, DURATION, _time))


func _draw() -> void:
    if _time <= 0.0 or _time >= DURATION:
        return
    for index in range(PARTICLE_COUNT):
        var start_time := 0.23 + float(index) * 0.041
        var lifetime := 0.48 + _fraction(float(index) * 0.381966) * 0.17
        var progress := (_time - start_time) / lifetime
        if progress <= 0.0 or progress >= 1.0:
            continue
        var fade := smoothstep(0.0, 0.16, progress) * (1.0 - smoothstep(0.83, 1.0, progress))
        fade *= 1.0 - smoothstep(1.72, 1.98, _time)
        var color := GOLD if index % 4 == 0 else VIOLET
        var tip := _particle_position(index, progress)
        # Short tails indicate inward motion without persistent actor ghosts.
        for segment in range(5):
            var recent := maxf(0.0, progress - float(segment) * 0.018)
            var older := maxf(0.0, progress - float(segment + 1) * 0.018)
            var a := _particle_position(index, recent)
            var b := _particle_position(index, older)
            var tail_alpha := fade * pow(1.0 - float(segment) / 5.0, 1.5)
            draw_line(a, b, Color(color, tail_alpha * 0.12), 0.008, true)
            draw_line(a, b, Color(color, tail_alpha * 0.7), 0.0025, true)
        _draw_glow(tip, Vector2.ONE * 0.014, Color(color, fade * 0.48))
        draw_circle(tip, 0.0030 if index % 4 == 0 else 0.0024,
            Color(Color(1.0, 0.89, 0.64) if index % 4 == 0 else Color(0.87, 0.69, 1.0), fade * 0.95), true, -1.0, true)
    var flash := get_flash_strength()
    if flash > 0.0:
        var expansion := 0.7 + 0.3 * smoothstep(1.985, 2.035, _time)
        _draw_glow(Vector2.ZERO, Vector2.ONE * 0.105 * expansion, Color(VIOLET, flash * 0.6))
        _draw_glow(Vector2.ZERO, Vector2(0.018, 0.083) * expansion, Color(0.86, 0.63, 1.0, flash * 0.8))
        _draw_glow(Vector2.ZERO, Vector2.ONE * 0.030, Color(GOLD, flash * 0.9))
        _draw_ray(Vector2(0.0, -0.071), flash)
        _draw_ray(Vector2(0.0, 0.071), flash)
        _draw_ray(Vector2(-0.044, 0.0), flash * 0.75)
        _draw_ray(Vector2(0.044, 0.0), flash * 0.75)
        draw_circle(Vector2.ZERO, 0.0055 * sqrt(flash), Color(1.0, 0.96, 0.84, flash), true, -1.0, true)


func _particle_position(index: int, progress: float) -> Vector2:
    var angle := float(index) * 2.399963
    var radius := 0.62 + _fraction(float(index) * 0.618034) * 0.20
    var start := Vector2(cos(angle) * radius * 0.72 + 0.20, sin(angle) * radius)
    var target := Vector2((_fraction(float(index) * 0.43) - 0.5) * 0.035,
        (_fraction(float(index) * 0.754878) - 0.5) * 0.24)
    # Endpoint contracts as the rift closes. Both seek and capture are history-free.
    target *= 1.0 - smoothstep(1.60, 1.96, _time)
    var bend := Vector2(-start.y, start.x) * (0.20 if index % 2 == 0 else -0.20)
    var eased := pow(progress, 1.65)
    return start.lerp(target, eased) + bend * sin(eased * PI)


func _draw_glow(center: Vector2, radius: Vector2, color: Color) -> void:
    draw_texture_rect(_glow, Rect2(center - radius, radius * 2.0), false, color)


func _draw_ray(end: Vector2, strength: float) -> void:
    for segment in range(4):
        var portion := float(segment) / 4.0
        draw_line(end * portion, end * (portion + 0.25),
            Color(1.0, 0.87, 0.64, strength * pow(1.0 - portion, 2.0)), 0.0022, true)


func _fraction(value: float) -> float:
    return value - floor(value)
