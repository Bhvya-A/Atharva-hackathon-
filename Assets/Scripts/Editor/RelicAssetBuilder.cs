#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheOtherSide.EditorTools
{
    /// <summary>
    /// Converts the imported relic models into normalized, hold-ready prefabs under
    /// Resources/Relics. Each prefab has a centered pivot, a real BoxCollider for the
    /// interaction ray, the RelicPickup component and a Void DimensionPresence.
    /// Materials are rebuilt as URP/Lit so the models render in this project instead
    /// of relying on the FBX-embedded Shader Graph materials.
    /// </summary>
    public static class RelicAssetBuilder
    {
        const string RelicFolder = "Assets/Resources/Relics";
        const string MaterialFolder = "Assets/Resources/Relics/Materials";

        const string SkullModel = "Assets/relic assest/Gece Studio/Free Skull/Mesh/SM_Skull01.fbx";
        const string BookModel = "Assets/relic assest/Grimoire/book.FBX";
        const string CandleModel = "Assets/relic assest/candle-holder/source/1234.fbx";
        const string CompassModel = "Assets/relic assest/pirate-compass/source/M_Compass_L.fbx";

        [MenuItem("The Other Side/Build Relic Prefabs", priority = 20)]
        public static void BuildAll()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder(RelicFolder);
            EnsureFolder(MaterialFolder);

            FixTextureImporters();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Material skullMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/relic assest/Gece Studio/Free Skull/Material/M_Skull_Color01.mat");
            Material bookMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/relic assest/Grimoire/Materials/book.mat");

            Material candleWax = BuildCandleWax();
            Material candleHolder = BuildCandleHolder();
            Material candleFlame = BuildCandleFlame();
            Material compass = BuildCompass();

            BuildPrefab(
                RelicType.Skull,
                SkullModel,
                0.30f,
                Quaternion.identity,
                new Dictionary<string, Material>(),
                skullMaterial);

            BuildPrefab(
                RelicType.Grimoire,
                BookModel,
                0.36f,
                Quaternion.identity,
                new Dictionary<string, Material>(),
                bookMaterial);

            BuildPrefab(
                RelicType.Candle,
                CandleModel,
                0.36f,
                Quaternion.identity,
                new Dictionary<string, Material>
                {
                    { "Cylinder001", candleHolder },
                    { "Cylinder002", candleHolder },
                    { "Cylinder005", candleHolder },
                    { "Cylinder003", candleWax },
                    { "Plane001", candleFlame },
                    { "Plane002", candleFlame }
                },
                null,
                new[] { "Plane001", "Plane002" });

            BuildPrefab(
                RelicType.Compass,
                CompassModel,
        // Pocket compass is tiny in source units; 0.16 m keeps it readable in hand.
                0.16f,
                Quaternion.Euler(0f, 0f, 0f),
                new Dictionary<string, Material>(),
                compass);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TheOtherSide] Relic prefabs rebuilt under " + RelicFolder + ".");
        }

        static void FixTextureImporters()
        {
            string[] normals =
            {
                "Assets/relic assest/Gece Studio/Free Skull/Textures/T_Skull_Normal.png",
                "Assets/relic assest/Grimoire/Materials/bookN.tif",
                "Assets/relic assest/candle-holder/textures/Material__31_normal.png",
                "Assets/relic assest/candle-holder/textures/Material__33_normal.png",
                "Assets/relic assest/pirate-compass/textures/Compass_Texture_normal.png"
            };

            foreach (string path in normals)
                SetTexture(path, true);

            string[] linear =
            {
                "Assets/relic assest/candle-holder/textures/Material__31_AO.jpg",
                "Assets/relic assest/candle-holder/textures/Material__31_metallic.jpg",
                "Assets/relic assest/candle-holder/textures/Material__31_roughness.jpg",
                "Assets/relic assest/candle-holder/textures/Material__33_AO.jpg",
                "Assets/relic assest/candle-holder/textures/Material__33_metallic.jpg",
                "Assets/relic assest/candle-holder/textures/Material__33_roughness.jpg",
                "Assets/relic assest/pirate-compass/textures/Compass_Texture_AO.jpg",
                "Assets/relic assest/pirate-compass/textures/Compass_Texture_metallic.jpg",
                "Assets/relic assest/pirate-compass/textures/Compass_Texture_roughness.jpg"
            };

            foreach (string path in linear)
                SetTexture(path, false);
        }

        static void SetTexture(string path, bool normalMap)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;

            bool changed = false;

            if (normalMap)
            {
                if (importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    changed = true;
                }

                if (importer.sRGBTexture)
                {
                    importer.sRGBTexture = false;
                    changed = true;
                }
            }
            else if (importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                changed = true;
            }

            if (changed)
                importer.SaveAndReimport();
        }

        static Material BuildCandleWax()
        {
            return CreateMaterial(
                "M_Candle_Wax",
                Tex("Assets/relic assest/candle-holder/textures/Material__31_albedo.jpg"),
                Tex("Assets/relic assest/candle-holder/textures/Material__31_normal.png"),
                Tex("Assets/relic assest/candle-holder/textures/Material__31_AO.jpg"),
                0.05f,
                0.42f,
                null,
                null);
        }

        static Material BuildCandleHolder()
        {
            return CreateMaterial(
                "M_Candle_Holder",
                Tex("Assets/relic assest/candle-holder/textures/Material__33_albedo.jpg"),
                Tex("Assets/relic assest/candle-holder/textures/Material__33_normal.png"),
                Tex("Assets/relic assest/candle-holder/textures/Material__33_AO.jpg"),
                0.85f,
                0.55f,
                null,
                null);
        }

        static Material BuildCandleFlame()
        {
            Texture2D emissive = Tex("Assets/relic assest/candle-holder/textures/Material__35_emissive.jpg");
            return CreateMaterial(
                "M_Candle_Flame",
                emissive,
                null,
                null,
                0f,
                0f,
                new Color(1f, 0.62f, 0.22f, 1f) * 3.2f,
                emissive);
        }

        static Material BuildCompass()
        {
            return CreateMaterial(
                "M_Compass",
                Tex("Assets/relic assest/pirate-compass/textures/Compass_Texture_albedo.jpg"),
                Tex("Assets/relic assest/pirate-compass/textures/Compass_Texture_normal.png"),
                Tex("Assets/relic assest/pirate-compass/textures/Compass_Texture_AO.jpg"),
                0.75f,
                0.62f,
                null,
                null);
        }

        static Texture2D Tex(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material CreateMaterial(
            string name,
            Texture2D albedo,
            Texture2D normal,
            Texture2D ao,
            float metallic,
            float smoothness,
            Color? emission,
            Texture2D emissionMap)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            if (albedo != null)
            {
                material.SetTexture("_BaseMap", albedo);
                material.SetTexture("_MainTex", albedo);
            }

            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);

            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
            }

            if (ao != null)
            {
                material.SetTexture("_OcclusionMap", ao);
                material.SetFloat("_OcclusionStrength", 1f);
                material.EnableKeyword("_OCCLUSIONMAP");
            }

            if (emission.HasValue)
            {
                material.SetColor("_EmissionColor", emission.Value);
                if (emissionMap != null)
                    material.SetTexture("_EmissionMap", emissionMap);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        static void BuildPrefab(
            RelicType type,
            string modelPath,
            float targetSize,
            Quaternion modelRotation,
            Dictionary<string, Material> materialOverrides,
            Material fallbackMaterial,
            string[] noShadowRenderers = null)
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null)
            {
                Debug.LogError($"[TheOtherSide] Missing relic model: {modelPath}");
                return;
            }

            GameObject root = new GameObject(type.ToString());

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = modelRotation;
            model.transform.localScale = Vector3.one;

            // Unpack so the saved prefab owns plain meshes instead of a nested FBX
            // instance with a long override list.
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material replacement = null;

                    if (materialOverrides != null &&
                        materialOverrides.TryGetValue(renderer.gameObject.name, out Material specific))
                    {
                        replacement = specific;
                    }
                    else if (fallbackMaterial != null)
                    {
                        replacement = fallbackMaterial;
                    }

                    if (replacement != null)
                        materials[i] = replacement;
                }

                renderer.sharedMaterials = materials;
            }

            if (noShadowRenderers != null)
            {
                foreach (Renderer renderer in renderers)
                {
                    for (int i = 0; i < noShadowRenderers.Length; i++)
                    {
                        if (renderer.gameObject.name == noShadowRenderers[i])
                        {
                            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            renderer.receiveShadows = false;
                        }
                    }
                }
            }

            // Normalize: fit the largest dimension to targetSize, then move the model
            // so its bounds center sits on the prefab origin. The pickup then spins
            // and bobs around the object's centre, and the hold profile can offset it
            // without fighting an arbitrary FBX pivot.
            Bounds bounds = ComputeBounds(model);
            float maxDimension = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = maxDimension > 0.0001f ? targetSize / maxDimension : 1f;
            model.transform.localScale = Vector3.one * scale;

            bounds = ComputeBounds(model);
            model.transform.localPosition -= bounds.center;

            bounds = ComputeBounds(model);

            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = bounds.size;
            collider.isTrigger = false;

            RelicPickup pickup = root.AddComponent<RelicPickup>();
            pickup.Configure(RelicTypes.DisplayName(type), type);

            root.AddComponent<DimensionPresence>().Configure(DimensionMembership.Void);

            if (type == RelicType.Candle)
                AddCandleLight(root.transform, bounds);

            string prefabPath = RelicFolder + "/" + type + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
        }

        static void AddCandleLight(Transform root, Bounds bounds)
        {
            GameObject lightGo = new GameObject("FlameLight");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(0f, bounds.max.y * 0.42f, 0f);

            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.38f);
            light.intensity = 0.9f;
            light.range = 3.2f;
            light.shadows = LightShadows.None;
        }

        static Bounds ComputeBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool has = false;
            Bounds bounds = new Bounds();

            foreach (Renderer renderer in renderers)
            {
                if (!has)
                {
                    bounds = renderer.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
