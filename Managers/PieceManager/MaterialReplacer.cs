﻿using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace PieceManager
{
    [PublicAPI]
    public static class MaterialReplacer
    {
        static MaterialReplacer()
        {
            originalMaterials = new Dictionary<string, Material>();
            _objectToSwap = new Dictionary<GameObject, bool>();
            _objectsForShaderReplace = new Dictionary<GameObject, ShaderType>();
            Harmony harmony = new("org.bepinex.helpers.PieceManager");
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ZoneSystem), nameof(ZoneSystem.Start)),
                postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(MaterialReplacer),
                    nameof(ReplaceAllMaterialsWithOriginal))));
        }

        public enum ShaderType
        {
            PieceShader,
            VegetationShader,
            RockShader,
            RugShader,
            GrassShader,
            CustomCreature,
            UseUnityShader
        }

        private static Dictionary<GameObject, bool> _objectToSwap;
        internal static Dictionary<string, Material> originalMaterials;
        private static Dictionary<GameObject, ShaderType> _objectsForShaderReplace;

        public static void RegisterGameObjectForShaderSwap(GameObject go, ShaderType type)
        {
            if (_objectsForShaderReplace.ContainsKey(go)) return;
            _objectsForShaderReplace?.Add(go, type);
        }

        public static void RegisterGameObjectForMatSwap(GameObject go, bool isJotunnMock = false)
        {
            if (_objectToSwap.ContainsKey(go)) return;
            _objectToSwap.Add(go, isJotunnMock);
        }

        private static void GetAllMaterials()
        {
            Material[]? allmats = Resources.FindObjectsOfTypeAll<Material>();
            foreach (Material? item in allmats)
            {
                originalMaterials[item.name] = item;
            }
        }


        [HarmonyPriority(Priority.VeryHigh)]
        private static void ReplaceAllMaterialsWithOriginal()
        {
            if (originalMaterials.Count <= 0) GetAllMaterials();
            foreach (Renderer? renderer in _objectToSwap.SelectMany(gameObject =>
                         gameObject.Key.GetComponentsInChildren<Renderer>(true)))
            {
                _objectToSwap.TryGetValue(renderer.gameObject, out bool jotunnPrefabFlag);
                Material[] newMats = new Material[renderer.sharedMaterials.Length];
                int i = 0;
                foreach (Material? t in renderer.sharedMaterials)
                {
                    string replacementString = jotunnPrefabFlag ? "JVLmock_" : "_REPLACE_";
                    if (!t.name.StartsWith(replacementString, StringComparison.Ordinal)) continue;
                    string matName = renderer.material.name.Replace(" (Instance)", string.Empty)
                        .Replace(replacementString, "");

                    string matNames = t.name.Replace(" (Instance)", string.Empty)
                        .Replace(replacementString, "");

                    if (originalMaterials.ContainsKey(matNames))
                    {
                        if (i <= renderer.materials.Length)
                        {
                            newMats[i] = originalMaterials[matNames];
                        }
                    }
                    else
                    {
                        Debug.LogWarning("No suitable material found to replace: " + matNames);
                        // Skip over this material in future
                        originalMaterials[matNames] = newMats[i];
                    }

                    if (originalMaterials.ContainsKey(matName))
                    {
                        renderer.material = originalMaterials[matName];
                    }
                    else
                    {
                        Debug.LogWarning("No suitable material found to replace: " + matName);
                        // Skip over this material in future
                        originalMaterials[matName] = renderer.material;
                    }

                    ++i;
                }

                renderer.materials = newMats;
                renderer.sharedMaterials = newMats;
            }

            foreach (Renderer? renderer in _objectsForShaderReplace.SelectMany(gameObject =>
                         gameObject.Key.GetComponentsInChildren<Renderer>(true)))
            {
                _objectsForShaderReplace.TryGetValue(renderer.gameObject.transform.root.gameObject,
                    out ShaderType shaderType);
                if (renderer == null) continue;
                foreach (Material? t in renderer.sharedMaterials)
                {
                    if (t == null) continue;
                    string name = t.shader.name;
                    switch (shaderType)
                    {
                        case ShaderType.PieceShader:
                            t.shader = Shader.Find("Custom/Piece");
                            break;
                        case ShaderType.VegetationShader:
                            t.shader = Shader.Find("Custom/Vegetation");
                            break;
                        case ShaderType.RockShader:
                            t.shader = Shader.Find("Custom/StaticRock");
                            break;
                        case ShaderType.RugShader:
                            t.shader = Shader.Find("Custom/Rug");
                            break;
                        case ShaderType.GrassShader:
                            t.shader = Shader.Find("Custom/Grass");
                            break;
                        case ShaderType.CustomCreature:
                            t.shader = Shader.Find("Custom/Creature");
                            break;
                        case ShaderType.UseUnityShader:
                            if (Shader.Find(name) != null)
                            {
                                t.shader = Shader.Find(name);
                            }

                            break;
                        default:
                            t.shader = Shader.Find("ToonDeferredShading2017");
                            break;
                    }
                }
            }
        }
    }
}