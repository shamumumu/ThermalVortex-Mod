// The shared face factory supplies complete Spire cards and card backs.
// Keep the argument for old callers, but never invent another card frame here.
vec4 fusion_card_surface(sampler2D card, vec2 uv, bool full_card) {
    return texture(card, uv);
}
