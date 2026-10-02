extends Node2D

var _center := Vector2.ZERO
var _radii := Vector2.ZERO
var _palm := Vector2.ZERO
var _contact := Vector2.ZERO
var _height := 0.0
var _growth := 0.0
var _age := -1.0
var _time := 0.0

func configure(center: Vector2, radii: Vector2, palm: Vector2, contact: Vector2,
        height: float, growth: float, age: float, effect_time: float) -> void:
    _center = center
    _radii = radii
    _palm = palm
    _contact = contact
    _height = height
    _growth = growth
    _age = age
    _time = effect_time
    queue_redraw()

func _hash(value: float) -> float:
    return fposmod(sin(value * 127.1 + 311.7) * 43758.5453, 1.0)

func _surface(angle: float, inset: float = 0.0) -> Vector2:
    return _center + Vector2(cos(angle) * maxf(0.0, _radii.x - inset), sin(angle) * maxf(0.0, _radii.y - inset))

func _surface_y(x: float) -> float:
    var normalized := clampf((x - _center.x) / _radii.x, -1.0, 1.0)
    return _center.y - _radii.y * sqrt(maxf(0.0, 1.0 - normalized * normalized))

# Project onto the visible curved membrane, between its crown and raised lip.
func _cap_point(x: float, depth: float) -> Vector2:
    var normalized := clampf(x, -0.995, 0.995)
    var arc := sqrt(maxf(0.0, 1.0 - normalized * normalized))
    return _center + Vector2(normalized * _radii.x, -arc * _radii.y * lerpf(1.0, 0.26, clampf(depth, 0.0, 1.0)))

func _draw() -> void:
    if _radii.x < 0.01 or _radii.y < 0.01:
        return
    var width := maxf(0.7, _height / 420.0)
    var flash := exp(-_age * 12.0) if _age >= 0.0 else 0.0
    _draw_rim(width, flash)
    _draw_lower_edge(width)
    _draw_surface_streams(width, flash)
    _draw_outer_wisps(width, flash)
    _draw_constellation(width)
    _draw_hand_energy(width)
    if _age >= 0.0:
        _draw_impact(width, flash)

func _draw_rim(width: float, flash: float) -> void:
    var points := PackedVector2Array()
    for i in range(113):
        points.append(_surface(PI + PI * i / 112.0))
    # One fine luminous edge. Broad light is transparent, not a second shell.
    draw_polyline(points, Color(0.56, 0.28, 1.0, 0.055 * _growth), width * 13.0, true)
    draw_polyline(points, Color(0.77, 0.49, 1.0, 0.11 * _growth), width * 5.5, true)
    for i in range(112):
        var fraction := (i + 0.5) / 112.0
        var shimmer := 0.76 + 0.12 * sin(fraction * 19.1 + 0.7) + 0.07 * sin(fraction * 57.0 - _time * 1.7)
        var gold := Color(1.0, 0.77, 0.44, (shimmer * 0.75 + flash * 0.13) * _growth)
        draw_line(points[i], points[i + 1], gold, width * 1.65, true)
        draw_line(points[i], points[i + 1], Color(1.0, 0.96, 0.84, shimmer * 0.80 * _growth), width * 0.62, true)

func _draw_lower_edge(width: float) -> void:
    var visibility := _growth * smoothstep(0.42, 0.78, _growth)
    for i in range(104):
        var x0 := -1.0 + 2.0 * i / 104.0
        var x1 := -1.0 + 2.0 * (i + 1) / 104.0
        var x := (x0 + x1) * 0.5
        var arc0 := sqrt(maxf(0.0, 1.0 - x0 * x0))
        var arc1 := sqrt(maxf(0.0, 1.0 - x1 * x1))
        var start := _center + Vector2(x0 * _radii.x, -0.26 * arc0 * _radii.y)
        var end := _center + Vector2(x1 * _radii.x, -0.26 * arc1 * _radii.y)
        var side := smoothstep(0.20, 0.95, absf(x))
        var alpha := (0.09 + 0.68 * side) * visibility
        draw_line(start, end, Color(0.83, 0.52, 1.0, alpha * 0.15), width * 4.5, true)
        draw_line(start, end, Color(1.0, 0.85, 0.59, alpha), width * 0.8, true)
        # The near return is just a dim violet hint; its middle disappears
        # behind the actor instead of enclosing the canopy in a solid ring.
        var return_start := _center + Vector2(x0 * _radii.x, 0.15 * arc0 * _radii.y)
        var return_end := _center + Vector2(x1 * _radii.x, 0.15 * arc1 * _radii.y)
        var return_alpha := (0.006 + 0.055 * side) * visibility
        draw_line(return_start, return_end, Color(0.63, 0.42, 0.97, return_alpha), width * 0.75, true)

func _draw_surface_streams(width: float, flash: float) -> void:
    # Unequal branching currents live on the membrane, not on concentric arcs.
    var phase_tick := floorf(_time * 8.0)
    for trail in range(6):
        var seed := float(trail) * 13.73 + 2.4
        var direction := -1.0 if trail % 2 else 1.0
        var travel := fposmod(_time * (0.075 + _hash(seed + 1.0) * 0.04) + _hash(seed + 2.0), 1.0)
        var start_x := lerpf(-0.86, 0.70, travel) * direction
        var span := (0.15 + _hash(seed + 3.0) * 0.24) * direction
        var depth := 0.09 + _hash(seed + 4.0) * 0.72
        var pulse := pow(maxf(0.0, sin(_time * (1.8 + _hash(seed + 5.0)) + seed)), 3.0)
        var intensity := (0.055 + pulse * 0.54 + flash * 0.24) * _growth
        var line := PackedVector2Array()
        for i in range(17):
            var amount := i / 16.0
            var jitter := (_hash(seed + i * 2.7 + phase_tick * 0.13) - 0.5) * sin(amount * PI)
            var x := start_x + span * amount
            var row := depth + sin(amount * PI * 1.35 + seed) * 0.052 + jitter * 0.067
            line.append(_cap_point(x, row))
        _draw_current(line, width, intensity, trail % 3 == 0)
        for fork in range(2):
            var junction := 5 + fork * 5
            var amount := junction / 16.0
            var branch := PackedVector2Array([line[junction]])
            var branch_sign := -1.0 if fork == 0 else 1.0
            for i in range(1, 7):
                var reach := i / 6.0
                var x := start_x + span * amount + span * reach * (0.20 + 0.08 * fork)
                var row := depth + branch_sign * reach * (0.10 + _hash(seed + fork) * 0.12)
                row += (_hash(seed + i * 3.1 + phase_tick * 0.19) - 0.5) * 0.037
                branch.append(_cap_point(x, row))
            _draw_current(branch, width * 0.64, intensity * 0.51, fork == 0 and trail % 2 == 0)

func _draw_current(points: PackedVector2Array, width: float, intensity: float, gold: bool) -> void:
    if points.size() < 2:
        return
    draw_polyline(points, Color(0.54, 0.25, 1.0, intensity * 0.20), width * 5.0, true)
    var core := Color(1.0, 0.83, 0.53, intensity * 0.76) if gold else Color(0.84, 0.72, 1.0, intensity)
    draw_polyline(points, core, width * 0.8, true)

func _draw_outer_wisps(width: float, flash: float) -> void:
    # A handful of unequal, branching filaments lift off the silhouette.
    # Each has its own phase and reach, so they never become a second rim.
    var starts := [0.12, 0.29, 0.475, 0.72, 0.89]
    var spans := [0.115, 0.058, 0.040, 0.083, 0.067]
    var tick := floorf(_time * 9.0)
    for wisp in range(starts.size()):
        var seed := 7.93 + wisp * 16.71
        var drift := sin(_time * (0.65 + _hash(seed)) + seed) * 0.010
        var start := PI * (1.0 + float(starts[wisp]) + drift)
        var span := PI * float(spans[wisp]) * (-1.0 if wisp % 2 else 1.0)
        var pulse := pow(0.5 + 0.5 * sin(_time * (2.0 + _hash(seed + 1.0)) + seed), 3.0)
        var alpha := (0.22 + 0.31 * pulse + 0.40 * flash) * _growth
        var line := PackedVector2Array()
        for i in range(19):
            var amount := i / 18.0
            var angle := start + span * amount
            var lift := width * (2.5 + sin(amount * PI) * (7.0 + _hash(seed + 2.0) * 11.0))
            lift += width * (_hash(seed + i * 4.17 + tick * 0.23) - 0.5) * 6.0
            line.append(_surface(angle, -maxf(width, lift)))
        draw_polyline(line, Color(0.55, 0.24, 1.0, alpha * 0.18), width * 7.0, true)
        draw_polyline(line, Color(0.76, 0.53, 1.0, alpha * 0.70), width * 1.45, true)
        draw_polyline(line, Color(0.93, 0.85, 1.0, alpha), width * 0.62, true)
        var branch := PackedVector2Array([line[8]])
        var branch_angle := start + span * (8.0 / 18.0)
        var normal := Vector2(cos(branch_angle), sin(branch_angle)).normalized()
        var tangent := Vector2(-normal.y, normal.x)
        for i in range(1, 7):
            var reach := i / 6.0
            var point := line[8] + normal * width * (8.0 + 8.0 * _hash(seed + 3.0)) * reach
            point += tangent * width * ((5.0 + 8.0 * _hash(seed + 4.0)) * reach + (_hash(seed + i * 3.7 + tick * 0.11) - 0.5) * 4.0)
            branch.append(point)
        draw_polyline(branch, Color(0.65, 0.32, 1.0, alpha * 0.13), width * 4.0, true)
        draw_polyline(branch, Color(0.87, 0.73, 1.0, alpha * 0.64), width * 0.64, true)
        # Brief warm streaks travel beside the purple wisps at different rates.
        var travel := fposmod(_time * (0.34 + 0.19 * _hash(seed + 5.0)) + _hash(seed + 6.0), 1.0)
        var streak_angle := start + span * travel
        var head := _surface(streak_angle, -width * (10.0 + 13.0 * _hash(seed + 7.0)))
        var direction := Vector2(-sin(streak_angle), cos(streak_angle)) * signf(span)
        var tail := head - direction * width * (3.0 + 6.0 * _hash(seed + 8.0))
        var streak_alpha := (0.18 + pulse * 0.44 + flash * 0.24) * _growth
        draw_line(tail, head, Color(1.0, 0.74, 0.34, streak_alpha), width * 0.7, true)
        draw_circle(head, width * 0.85, Color(1.0, 0.96, 0.71, streak_alpha))

func _draw_constellation(width: float) -> void:
    for mote in range(24):
        var seed := 5.71 + mote * 9.173
        var phase := _time * (1.2 + _hash(seed) * 1.9) + seed
        var pulse := pow(maxf(0.0, sin(phase)), 7.0)
        var brightness := (0.04 + pulse * (0.44 + _hash(seed + 1.0) * 0.4)) * _growth
        var point: Vector2
        if mote % 3 == 0:
            var angle := PI * (1.035 + 0.93 * _hash(seed + 2.0))
            point = _surface(angle, -width * (2.0 + 9.0 * _hash(seed + 3.0)))
        else:
            point = _cap_point(lerpf(-0.89, 0.89, _hash(seed + 2.0)), 0.06 + 0.83 * _hash(seed + 3.0))
        var size := width * (0.4 + _hash(seed + 4.0) * 0.6)
        var ink := Color(1.0, 0.84, 0.48, brightness) if mote % 4 else Color(0.78, 0.65, 1.0, brightness)
        if mote % 5 == 1 and pulse > 0.08:
            draw_circle(point, width * 5.0, Color(0.70, 0.44, 1.0, brightness * 0.045))
            _star(point, width * (2.1 + pulse * 2.4), ink, _hash(seed + 5.0) * 0.45)
        else:
            draw_circle(point, size, ink)

func _draw_hand_energy(width: float) -> void:
    var glow := (0.38 + 0.10 * sin(_time * 2.7)) * _growth
    draw_circle(_palm, width * 6.0, Color(0.86, 0.48, 1.0, glow * 0.065))
    _star(_palm, width * 4.0, Color(1.0, 0.91, 0.61, glow))
    var normalized := (_palm.x - _center.x) / _radii.x
    if absf(normalized) > 0.97:
        return
    var support := Vector2(_palm.x, _surface_y(_palm.x))
    var formed := _growth * smoothstep(0.68, 0.96, _growth)
    var energy := (0.80 + 0.12 * sin(_time * 2.3 + 0.6)) * formed
    draw_circle(support, width * 15.0, Color(0.61, 0.29, 1.0, energy * 0.040))
    draw_circle(support, width * 9.0, Color(0.77, 0.49, 1.0, energy * 0.085))
    draw_circle(support, width * 5.0, Color(1.0, 0.77, 0.43, energy * 0.15))
    _star(support, width * 10.0, Color(1.0, 0.82, 0.45, energy * 0.86), 0.13)
    _star(support, width * 5.6, Color(1.0, 0.98, 0.84, energy), -0.07)
    draw_circle(support, width * 2.7, Color(1.0, 1.0, 0.94, energy * 0.94))
    for mote in range(4):
        var amount := fposmod(_time * (0.48 + mote * 0.07) + mote * 0.237, 1.0)
        var point := _palm.lerp(support, amount)
        point.x += sin(amount * PI) * sin(amount * TAU * 1.4 + mote * 2.1) * width * 2.2
        var alpha := sin(amount * PI) * (0.48 + 0.12 * sin(_time * 3.0 + mote)) * formed
        draw_circle(point, width * (0.6 + 0.25 * amount), Color(1.0, 0.91, 0.60, alpha))

func _draw_impact(width: float, flash: float) -> void:
    var residual := exp(-_age * 4.8)
    var normalized := Vector2((_contact.x - _center.x) / _radii.x, (_contact.y - _center.y) / _radii.y)
    var normal := Vector2(normalized.x / _radii.x, normalized.y / _radii.y).normalized()
    var tangent := Vector2(-normal.y, normal.x)
    for layer in range(4, 0, -1):
        draw_circle(_contact, width * (6.0 + layer * 6.0), Color(0.84, 0.65, 1.0, flash * 0.045))
    _star(_contact, width * (20.0 + flash * 13.0), Color(1.0, 0.91, 0.65, flash * 0.90), tangent.angle() + 0.17)
    _star(_contact, width * (11.0 + flash * 8.0), Color(1.0, 1.0, 0.94, flash), tangent.angle() - 0.37)
    if _age < 0.95:
        _draw_impact_branches(width, residual)
        for spark in range(19):
            var seed := spark * 4.719 + 0.83
            var angle := normal.angle() + lerpf(-1.28, 1.28, _hash(seed))
            var velocity := Vector2.from_angle(angle) * width * (26.0 + 105.0 * _hash(seed + 1.0))
            var point := _contact + velocity * _age + Vector2(0.0, _age * _age * width * 17.0)
            var length := width * (1.3 + 4.8 * _hash(seed + 2.0))
            var tail := point - velocity.normalized() * length
            var ink := Color(1.0, 0.82, 0.43, residual * 0.85) if spark % 4 else Color(0.78, 0.55, 1.0, residual * 0.75)
            draw_line(tail, point, ink, width * (0.60 + _hash(seed + 3.0) * 0.5), true)
    _draw_surface_wave(width, tangent, normal, 0.0, 1.0)
    _draw_surface_wave(width, tangent, normal, 0.13, 0.52)

func _draw_impact_branches(width: float, intensity: float) -> void:
    var contact_x := (_contact.x - _center.x) / _radii.x
    for side: float in [-1.0, 1.0]:
        var line := PackedVector2Array([_contact])
        for i in range(1, 19):
            var amount := i / 18.0
            var x := contact_x + side * amount * (0.30 + 0.16 * (1.0 - exp(-_age * 8.0)))
            var depth := amount * 0.22 + (_hash(i * 2.38 + side + floorf(_age * 16.0)) - 0.5) * 0.05
            line.append(_cap_point(x, maxf(0.0, depth)))
        _draw_current(line, width * 1.35, intensity * 0.92, side > 0.0)
        var branch := PackedVector2Array([line[7]])
        for i in range(1, 9):
            var amount := i / 8.0
            var x := contact_x + side * (0.145 + amount * 0.11)
            var depth := 0.085 + amount * 0.25 + sin(i * 9.7 + floorf(_age * 11.0)) * 0.02
            branch.append(_cap_point(x, depth))
        _draw_current(branch, width * 0.85, intensity * 0.51, side < 0.0)

func _draw_surface_wave(width: float, tangent: Vector2, normal: Vector2, delay: float, strength: float) -> void:
    var age := _age - delay
    if age < 0.0 or age >= 0.70:
        return
    var radius := _height * (0.018 + age * 0.40)
    var fade := exp(-age * 5.5) * (1.0 - smoothstep(0.45, 0.70, age)) * strength
    var previous := Vector2.ZERO
    for i in range(89):
        var angle := TAU * i / 88.0
        var along := cos(angle) * radius
        var across := sin(angle) * radius * 0.22
        var x := clampf(_contact.x + tangent.x * along - normal.x * across, _center.x - _radii.x + 0.5, _center.x + _radii.x - 0.5)
        # Bending the long axis onto the canopy follows its local slope and
        # curvature; the short axis is foreshortened into a surface ellipse.
        var point := Vector2(x, _surface_y(x) - normal.y * across)
        if i > 0:
            var shimmer := 0.38 + 0.62 * pow(0.5 + 0.5 * sin(angle * 2.0 + delay * 13.0), 2.0)
            var alpha := fade * shimmer
            draw_line(previous, point, Color(0.78, 0.53, 1.0, alpha * 0.14), width * 4.0, true)
            draw_line(previous, point, Color(1.0, 0.88, 0.64, alpha * 0.80), width * 0.85, true)
        previous = point

func _star(point: Vector2, size: float, color: Color, rotation: float = 0.0) -> void:
    var star := PackedVector2Array()
    for i in range(8):
        var angle := PI * i / 4.0 + rotation
        var radius := size * (1.0 if i % 4 == 0 else 0.78) if i % 2 == 0 else size * 0.09
        star.append(point + Vector2(cos(angle), sin(angle)) * radius)
    draw_colored_polygon(star, color)

