shader_type canvas_item;
render_mode unshaded;

uniform float growth = 1.0;
uniform float impact_age = -1.0;
uniform float effect_time = 0.0;
uniform vec2 contact_uv = vec2(0.0, -1.0);
uniform vec2 dome_center = vec2(0.0);
uniform vec2 dome_radii = vec2(1.0);
varying vec2 film_position;
varying vec4 canvas_tint;

void vertex() {
    film_position = VERTEX;
    canvas_tint = COLOR;
}

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 cell = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(cell), hash(cell + vec2(1.0, 0.0)), f.x),
        mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0, 1.0)), f.x), f.y);
}

float mist(vec2 p) {
    float sum = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 4; i++) {
        sum += noise(p) * amplitude;
        p = mat2(vec2(1.66, 1.19), vec2(-1.19, 1.66)) * p + vec2(4.1, 7.3);
        amplitude *= 0.5;
    }
    return sum;
}

vec2 facets(vec2 p) {
    vec2 base = floor(p);
    vec2 f = fract(p);
    float first = 8.0;
    float second = 8.0;
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 cell = vec2(float(x), float(y));
            vec2 jitter = vec2(hash(base + cell), hash(base + cell + vec2(13.2, 7.9)));
            float d = length(cell + 0.10 + jitter * 0.80 - f);
            if (d < first) { second = first; first = d; }
            else if (d < second) { second = d; }
        }
    }
    return vec2(first, second);
}

void fragment() {
    vec2 q = (film_position - dome_center) / max(dome_radii, vec2(0.001));
    float radial = length(q);
    float width_mask = 1.0 - smoothstep(0.997, 1.010, abs(q.x));
    float span = sqrt(max(0.0, 1.0 - q.x * q.x));
    float lip = -0.26 * span;
    float return_lip = 0.15 * span;
    float outer = 1.0 - smoothstep(0.997, 1.007, radial);
    float above_lip = 1.0 - smoothstep(lip - 0.003, lip + 0.005, q.y);
    float film_mask = outer * above_lip * width_mask;

    // Surface coordinates on a tilted sphere. The bright lip bows upward as
    // viewed from below; the shaded opening is behind the central character.
    float depth = sqrt(max(0.0, 1.0 - dot(q, q)));
    float tilt_sin = 0.26;
    float tilt_cos = 0.965609;
    vec3 surface = normalize(vec3(q.x, tilt_sin * q.y - tilt_cos * depth,
        -tilt_cos * q.y - tilt_sin * depth));
    vec2 sheet = vec2(atan(surface.x, -surface.y), surface.z * 1.5);
    float time = effect_time;
    vec2 current = vec2(time * 0.035, -time * 0.022);
    vec2 warp = vec2(mist(sheet * 3.1 + current), mist(sheet * 3.6 + vec2(9.4, -time * 0.04)));
    vec2 flow = sheet * vec2(9.0, 10.5) + (warp - 0.5) * 2.0 + current;
    vec2 cell = facets(flow);
    float cracks = exp(-(cell.y - cell.x) * 21.0);
    float vapour = mist(sheet * 5.2 + warp * 2.7 + current);
    float flecks = noise(flow * 3.0 + warp);
    float refraction = pow(max(0.0, 1.0 - abs(vapour * 2.0 - 1.04)), 9.0);
    float crest = smoothstep(0.47, 0.79, vapour);
    float caustics = cracks * (0.22 + crest * 0.64) + refraction * 0.30;
    float shimmer = 0.68 + 0.32 * sin(sheet.x * 7.0 + sheet.y * 11.0 + time * 1.7);
    float gold_veins = cracks * smoothstep(0.53, 0.76, vapour) * shimmer;

    float grazing = pow(1.0 - depth, 2.2);
    float crown = pow(max(0.0, -q.y), 2.7);
    float rim = exp(-abs(radial - 0.997) * 180.0);
    float rim_sheen = exp(-abs(radial - 0.98) * 28.0);
    float arc_illumination = 0.70 + 0.30 * sin(sheet.x * 5.0 + time * 0.5);
    float lip_glimmer = exp(-abs(q.y - lip) * 165.0) * (0.20 + pow(abs(q.x), 1.7));

    vec3 origin = normalize(vec3(contact_uv.x, tilt_sin * contact_uv.y, -tilt_cos * contact_uv.y));
    float geodesic = acos(clamp(dot(surface, origin), -1.0, 1.0));
    float waves = 0.0;
    float gold_waves = 0.0;
    float impact_glow = 0.0;
    if (impact_age >= 0.0) {
        impact_glow = exp(-length((q - contact_uv) * vec2(1.0, 1.2)) * 8.0) * exp(-impact_age * 8.0);
        for (int i = 0; i < 3; i++) {
            float age = impact_age - float(i) * 0.095;
            if (age >= 0.0) {
                float wave = exp(-abs(geodesic - age * 2.65) * 61.0) * exp(-impact_age * 2.5);
                waves += wave;
                gold_waves += wave * (i == 0 ? 0.80 : 0.24);
            }
        }
    }

    vec3 gold = vec3(1.0, 0.78, 0.38);
    vec3 lavender = vec3(0.77, 0.59, 1.0);
    vec3 purple = mix(vec3(0.25, 0.085, 0.51), vec3(0.69, 0.49, 0.95), vapour * 0.82 + crown * 0.17);
    vec3 film_color = purple + lavender * (caustics * 0.57 + refraction * 0.24 + rim_sheen * 0.43)
        + gold * (gold_veins * 1.02 + rim * arc_illumination * 1.7 + lip_glimmer * 0.46 + gold_waves * 0.68)
        + vec3(0.86, 0.71, 1.0) * (waves * 0.92 + impact_glow * 1.6);
    float film_alpha = (0.26 + vapour * 0.18 + grazing * 0.15 + crown * 0.095
        + caustics * 0.16 + flecks * 0.018 + rim * 0.44 + lip_glimmer * 0.08
        + waves * 0.31 + impact_glow * 0.33) * film_mask;

    float opening = smoothstep(lip - 0.004, lip + 0.004, q.y)
        * (1.0 - smoothstep(return_lip - 0.003, return_lip + 0.008, q.y)) * width_mask;
    float return_edge = exp(-abs(q.y - return_lip) * 125.0);
    float shade_alpha = (0.025 + 0.033 * pow(abs(q.x), 2.0) + return_edge * 0.033) * opening;
    vec3 shade_color = vec3(0.26, 0.13, 0.43);
    float halo = exp(-abs(radial - 1.0) * 50.0) * (1.0 - smoothstep(-0.01, 0.04, q.y));
    float halo_alpha = halo * 0.16;
    vec3 halo_color = vec3(0.66, 0.36, 1.0);
    float alpha = film_alpha + shade_alpha + halo_alpha;
    vec3 color = (film_color * film_alpha + shade_color * shade_alpha + halo_color * halo_alpha)
        / max(alpha, 0.0001);
    COLOR = vec4(color, min(alpha, 0.95) * growth) * canvas_tint;
}
