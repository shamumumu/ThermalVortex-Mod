shader_type canvas_item;
render_mode unshaded, blend_add;

uniform sampler2D swirl_009 : filter_linear_mipmap, repeat_disable;
uniform sampler2D swirl_010 : filter_linear_mipmap, repeat_disable;
uniform sampler2D swirl_011 : filter_linear_mipmap, repeat_disable;
uniform sampler2D swirl_012 : filter_linear_mipmap, repeat_disable;
uniform sampler2D material_uzu : filter_linear_mipmap, repeat_disable;
uniform sampler2D gradient_mask : filter_linear, repeat_disable;
uniform vec2 viewport_size = vec2(1280.0, 720.0);
uniform float main_time = -1.0;
uniform float intro_time = 0.0;
uniform float intro_mask_offset = 0.0;
uniform float intro_amplitude = 1.0;
uniform float global_alpha = 1.0;

vec2 rot(vec2 p, float a) {
    return vec2(cos(a) * p.x - sin(a) * p.y, sin(a) * p.x + cos(a) * p.y);
}
float soft_ring(float radius, float center, float width) {
    return exp(-pow((radius - center) / width, 2.0));
}

void fragment() {
    vec2 p = (UV - 0.5) * vec2(viewport_size.x / viewport_size.y, 1.0);
    float r = length(p);
    float angle = atan(p.y, p.x);
    vec3 red = vec3(1.0, 0.065, 0.16);
    vec3 blue = vec3(0.12, 0.23, 1.0);
    vec3 violet = vec3(0.47, 0.09, 0.87);
    vec3 light = vec3(0.0);
    if (main_time < 0.0) {
        float env = smoothstep(0.02, 0.32, intro_time) * (1.0 - smoothstep(1.12, 1.333333, intro_time));
        vec2 uzu_uv = p / 0.83 + 0.5;
        float uzu = texture(material_uzu, uzu_uv).a;
        float mask = texture(gradient_mask, rot(p / 0.83, -intro_time * 3.0) + 0.5).r;
        // The original also gates the offset-adjusted color by its raw alpha.
        // Without this second factor, offset=1 fills the whole quad solid.
        float raw_alpha = mask * uzu;
        float recovered_mask = clamp(raw_alpha + intro_mask_offset * 2.0 - 1.0, 0.0, 1.0) * raw_alpha;
        light = mix(red, blue, 0.5 + 0.5 * sin(angle * 2.0)) * recovered_mask * intro_amplitude * 0.34 * env;
        light += violet * exp(-r * r * 12.0) * 0.075 * env;
    } else {
        float t = main_time;
        float spin = t * 3.5 + smoothstep(0.45, 1.45, t) * 3.8;
        float contraction = mix(1.0, 0.67, smoothstep(0.75, 1.5, t));
        vec2 q = p / (0.98 * contraction);
        float grow = smoothstep(0.0, 0.26, t);
        float vortex_env = grow * (1.0 - smoothstep(1.05, 1.2, t));
        // Four texture layers correspond to Red / RedB / Blue / BlueB tracks.
        // Emitter geometry was absent; their angular motion is reconstructed.
        float red_a = texture(swirl_009, rot(q, spin) + 0.5).a;
        float red_b = texture(swirl_011, rot(q * 1.12, spin * 0.83 + 1.7) + 0.5).a;
        float blue_a = texture(swirl_010, rot(q, spin + 3.14159265) + 0.5).a;
        float blue_b = texture(swirl_012, rot(q * 1.12, spin * 0.83 + 4.84) + 0.5).a;
        float split = 0.5 + 0.5 * sin(angle * 2.0 + r * 15.0 - spin * 1.8);
        light += (red * (red_a * 0.67 + red_b * 0.22) * (0.36 + split * 0.64));
        light += (blue * (blue_a * 0.67 + blue_b * 0.22) * (1.0 - split * 0.64));
        float spiral = pow(max(0.0, sin(angle * 2.0 + r * 23.0 - spin * 2.0)), 12.0);
        float band = smoothstep(0.06, 0.17, r) * (1.0 - smoothstep(0.31, 0.49, r));
        light += mix(blue, red, split) * spiral * band * 0.26;
        light *= vortex_env;
        float charge = smoothstep(1.1, 1.73, t) * (1.0 - smoothstep(1.83, 2.14, t));
        float core_radius = mix(0.12, 0.055, smoothstep(1.15, 1.8, t));
        light += violet * exp(-r * r / (core_radius * core_radius * 3.0)) * charge * 0.72;
        light += vec3(0.48, 0.27, 1.0) * soft_ring(r, core_radius, 0.008) * charge * 0.64;
        float burst_age = t - 1.8;
        float burst = smoothstep(0.0, 0.035, burst_age) * (1.0 - smoothstep(0.12, 0.65, burst_age));
        float wave_radius = 0.05 + max(0.0, burst_age) * 0.91;
        light += vec3(0.50, 0.34, 1.0) * soft_ring(r, wave_radius, 0.006 + max(0.0, burst_age) * 0.012) * burst * 0.67;
        float rays = pow(abs(cos(angle * 7.0 + 0.3)), 44.0);
        light += vec3(0.37, 0.19, 0.9) * rays * exp(-r * 5.5) * burst * 0.48;
        light += vec3(0.63, 0.51, 1.0) * exp(-r * r * 120.0) * burst * 0.60;
        float after = smoothstep(1.8, 2.0, t) * (1.0 - smoothstep(2.55, 3.0333333, t));
        light += violet * texture(material_uzu, rot(p / 0.66, -t * 1.2) + 0.5).a * after * 0.20;
    }
    light *= 1.0 - smoothstep(0.6, 0.86, r);
    COLOR = vec4(light * global_alpha, global_alpha);
}
