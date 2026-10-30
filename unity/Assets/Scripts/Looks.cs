using System.Collections.Generic;
using BulletHeaven.Levels;
using UnityEngine;

namespace BulletHeaven.Unity
{
    /// <summary>Materials: everything solid uses the BulletHeaven/Lit shader (Resources/Shaders).</summary>
    public static class Looks
    {
        private static Shader _lit;
        private static Shader _additive;
        private static readonly Dictionary<Material, Material> Converted = new Dictionary<Material, Material>();

        public static Shader LitShader => _lit != null ? _lit : _lit = Shader.Find("BulletHeaven/Lit");
        public static Shader AdditiveShader => _additive != null ? _additive : _additive = Shader.Find("BulletHeaven/Additive");

        public static Material Plastic(Color color, float roughness = 0.6f, float metallic = 0f)
        {
            var material = new Material(LitShader) { enableInstancing = true };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Roughness", roughness);
            material.SetFloat("_Metallic", metallic);
            return material;
        }

        /// <summary>Self-lit (bloom picks it up): windows, tracers, lamp shades.</summary>
        public static Material Glow(Color color, float energy)
        {
            var material = Plastic(Color.black, 1f);
            material.SetVector("_EmissionColor", (Vector4)(color.linear * energy));
            return material;
        }

        public static Material FromLook(Look look) =>
            look.Glow > 0f ? Glow(look.Color.ToUnity(), look.Glow) : Plastic(look.Color.ToUnity(), look.Roughness, look.Metallic);

        public static Material Additive(Color color)
        {
            var material = new Material(AdditiveShader) { enableInstancing = true };
            material.SetColor("_Color", color);
            return material;
        }

        /// <summary>
        /// A BulletHeaven/Lit copy of a material glTFast imported (its textures follow glTF: roughness
        /// in G and metallic in B). Cached per source; callers copy it again when they need their own.
        /// </summary>
        public static Material FromGltf(Material source)
        {
            if (source == null)
                return Plastic(Color.white);
            if (Converted.TryGetValue(source, out var done))
                return done;

            var material = new Material(LitShader) { name = source.name, enableInstancing = true };
            var albedo = Texture(source, "baseColorTexture", "_BaseMap", "_MainTex");
            if (albedo != null)
                material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", Colour(source, Color.white, "baseColorFactor", "_BaseColor", "_Color"));

            var orm = Texture(source, "metallicRoughnessTexture", "_MetallicGlossMap");
            if (orm != null)
                material.SetTexture("_OrmMap", orm);
            material.SetFloat("_Roughness", Number(source, 1f, "roughnessFactor"));
            material.SetFloat("_Metallic", Number(source, orm != null ? 1f : 0f, "metallicFactor", "_Metallic"));

            var normal = Texture(source, "normalTexture", "_BumpMap");
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_HasNormalMap", 1f);
            }

            var emission = Texture(source, "emissiveTexture", "_EmissionMap");
            var emissive = Colour(source, Color.black, "emissiveFactor", "_EmissionColor");
            if (emission != null || emissive.maxColorComponent > 0.001f)
            {
                if (emission != null)
                    material.SetTexture("_EmissionMap", emission);
                material.SetVector("_EmissionColor", (Vector4)emissive.linear);
            }

            Converted[source] = material;
            return material;
        }

        private static Texture Texture(Material m, params string[] names)
        {
            foreach (var name in names)
            {
                if (m.HasProperty(name) && m.GetTexture(name) is Texture texture)
                    return texture;
            }
            return null;
        }

        private static Color Colour(Material m, Color fallback, params string[] names)
        {
            foreach (var name in names)
            {
                if (m.HasProperty(name))
                    return m.GetColor(name);
            }
            return fallback;
        }

        private static float Number(Material m, float fallback, params string[] names)
        {
            foreach (var name in names)
            {
                if (m.HasProperty(name))
                    return m.GetFloat(name);
            }
            return fallback;
        }
    }
}
