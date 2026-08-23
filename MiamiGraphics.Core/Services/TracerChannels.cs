using System;
using System.Collections.Generic;
using System.Linq;

namespace MiamiGraphics.Core.Services
{
    public static class TracerChannels
    {
        public sealed class Channel
        {
            public string EffectRule { get; init; } = "";

            public string RuLabel { get; init; } = "";

            public IReadOnlyList<string> ParticleRules { get; init; } = Array.Empty<string>();

            public IReadOnlyList<string> ExclusiveParticleRules { get; init; } = Array.Empty<string>();

            public IReadOnlyList<string> Textures { get; init; } = Array.Empty<string>();

            public bool RequiresCustomCore { get; init; }

            public float BaseThickness { get; init; }
            public float BaseLength { get; init; }

            public IReadOnlyList<string> BodyRules =>
                ExclusiveParticleRules.Where(r => r.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) < 0).ToList();

            public IReadOnlyList<string> SmokeRules =>
                ExclusiveParticleRules.Where(r => r.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        public static readonly IReadOnlyList<Channel> All = new[]
        {
            new Channel
            {
                EffectRule = "bullet_tracer",
                BaseThickness = 0.6f,
                BaseLength = 500f,
                RuLabel    = "Обычный",
                ParticleRules          = new[] { "bullet_tracer", "bullet_tracer_smoke" },
                ExclusiveParticleRules = new[] { "bullet_tracer", "bullet_tracer_smoke" },
                Textures   = new[] { "ptfx_bullet_tracer" },
            },
            new Channel
            {
                EffectRule = "bullet_tracer_mg",
                BaseThickness = 8f,
                BaseLength = 100f,
                RuLabel    = "Пулемётный",
                ParticleRules          = new[] { "bullet_tracer_lazer", "bullet_tracer_lazer_smoke" },
                ExclusiveParticleRules = new[] { "bullet_tracer_lazer" },
                Textures   = new[] { "ptfx_bullet_tracer" },
            },
            new Channel
            {
                EffectRule = "bullet_tracer_railgun",
                BaseThickness = 0.6f,
                BaseLength = 500f,
                RuLabel    = "Рельса",
                ParticleRules          = new[] { "bullet_rg_tracer", "bullet_tracer_rg_smoke", "bullet_tracer_lazer_smoke" },
                ExclusiveParticleRules = new[] { "bullet_rg_tracer", "bullet_tracer_rg_smoke" },
                Textures   = new[] { "ptfx_bullet_tracer_rg", "ptfx_bullet_tacer_heat" },
            },
            new Channel
            {
                EffectRule = "bullet_shotgun_tracer",
                BaseThickness = 0.4f,
                BaseLength = 0.501675f,
                RuLabel    = "Дробовик",
                ParticleRules          = new[] { "bullet_sg_tracer", "bullet_sg_tracer_smoke" },
                ExclusiveParticleRules = new[] { "bullet_sg_tracer", "bullet_sg_tracer_smoke" },
                Textures   = new[] { "ptfx_bullet_tracer" },
            },
            new Channel
            {
                EffectRule = "bullet_tracer_jet",
                BaseThickness = 4f,
                BaseLength = 30f,
                RuLabel    = "Реактивный",
                ParticleRules          = new[] { "bullet_tracer_jet", "bullet_tracer_jet_smoke", "bullet_tracer_jet_heat" },
                ExclusiveParticleRules = new[] { "bullet_tracer_jet", "bullet_tracer_jet_smoke", "bullet_tracer_jet_heat" },
                Textures   = new[] { "ptfx_bullet_tracer", "ptfx_bullet_tacer_heat" },
            },
        };

        public static readonly IReadOnlyList<Channel> CustomCoreSlots = new[]
        {
            new Channel
            {
                EffectRule = "yampai_h",
                RuLabel    = "Свой слот 1",
                RequiresCustomCore     = true,
                ParticleRules          = new[] { "yampai_h" },
                ExclusiveParticleRules = new[] { "yampai_h" },
                Textures   = new[] { "ptfx_yampai_tracer" },
            },
            new Channel
            {
                EffectRule = "yampai_m",
                RuLabel    = "Свой слот 2",
                RequiresCustomCore     = true,
                ParticleRules          = new[] { "yampai_m" },
                ExclusiveParticleRules = new[] { "yampai_m" },
                Textures   = new[] { "ptfx_yampai_tracer" },
            },
        };

        public static Channel? ByEffectRule(string? effectRule)
            => string.IsNullOrWhiteSpace(effectRule)
                ? null
                : All.Concat(CustomCoreSlots)
                    .FirstOrDefault(c => c.EffectRule.Equals(effectRule, StringComparison.OrdinalIgnoreCase));

        public const string SharedShapeTexture = "ptfx_bullet_tracer";
    }
}
