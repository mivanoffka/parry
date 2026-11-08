using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Parry.Editor
{
    // Batch check: Unity -batchmode -projectPath <project> -executeMethod Parry.Editor.CombatRenderingCheck.Run -quit
    public static class CombatRenderingCheck
    {
        [MenuItem("Parry/Validate Rendering")]
        public static void Run()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (string key in new[] { "player", "target", "sword" })
                {
                    string path = "Assets/Resources/Combat/" + key + "_normal.png";
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (texture == null || importer.sRGBTexture || importer.mipmapEnabled || texture.height != 64)
                        throw new Exception("Invalid linear normal-map import: " + path);
                    Debug.Log("NORMAL MAP OK: " + path);
                }
                foreach (string name in new[] { "SpriteNormalLit", "MetalSparks" })
                {
                    var shader = Resources.Load<Shader>("Combat/" + name);
                    if (shader == null || !shader.isSupported) throw new Exception("Missing or unsupported shader: " + name);
                    var material = new Material(shader);
                    // This overload compiles synchronously when available on the installed Unity editor.
                    var compile = typeof(ShaderUtil).GetMethod("CompilePass", BindingFlags.Public | BindingFlags.Static,
                        null, new[] { typeof(Material), typeof(int), typeof(bool) }, null);
                    if (compile == null) throw new Exception("Synchronous shader compilation API unavailable");
                    foreach (string keyword in new[] { "", "_ADDITIONAL_LIGHTS", "_CLUSTER_LIGHT_LOOP" })
                    {
                        material.shaderKeywords = keyword.Length == 0 ? Array.Empty<string>() : new[] { keyword };
                        compile.Invoke(null, new object[] { material, 0, true });
                        var errors = ShaderUtil.GetShaderMessages(shader).Where(message => message.severity.ToString() == "Error").ToArray();
                        if (errors.Length > 0) throw new Exception(string.Join("\n", errors.Select(error => error.message)));
                    }
                    UnityEngine.Object.DestroyImmediate(material);
                    Debug.Log("SHADER COMPILED OK: " + name);
                }
                Debug.Log("COMBAT RENDERING CHECK PASSED");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }
    }
}
