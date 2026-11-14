using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace hfcRed.ShaderToAnimatorBridge
{
    public enum EmitterSource { LightLevelExample, Template, ExistingShader }

    public static class BridgeInstancer
    {
        public const string SourceName = "Shader Bridge";
        const string SourcePrefix = "ShaderBridge/";
        const string SourceSync = SourcePrefix + "Synced";
        const int SourceBits = 8;

        const string IncludeGuid = "6b7f9a9a4631442aa48e744fc83348ce";
        const string LightLevelGuid = "1b5ea851f51943ce8ce87738ad33ac23";
        const string TemplateGuid = "0e610cd4107140649de9865668e3bc1e";
        const string SourceControllerGuid = "561d9c33573c00c408660982ac5405bb";

        public static string SourceFolder => Parent(AssetDatabase.GUIDToAssetPath(SourceControllerGuid));

        public static string PackageFolder
        {
            get
            {
                for (string folder = SourceFolder; !string.IsNullOrEmpty(folder); folder = Parent(folder))
                    if (File.Exists(folder + "/package.json")) return folder;
                return SourceFolder;
            }
        }

        public static string Folder(string parent, BridgeSettings s) => $"{parent}/{s.name}";
        public static string PrefabPath(string folder, BridgeSettings s) => $"{folder}/{s.name}.prefab";
        public static string ControllerPath(string folder, BridgeSettings s) => $"{folder}/{s.name} FX.controller";
        public static string ParametersPath(string folder, BridgeSettings s) => $"{folder}/{s.name} Parameters.asset";
        public static string EmitterShaderPath(string folder, BridgeSettings s) => $"{folder}/Emitter/{s.name} Emitter.shader";
        static string MaterialPath(string folder, BridgeSettings s) => $"{folder}/Emitter/{s.name} Emitter.mat";
        static string HeadMeshPath(string folder) => $"{folder}/Emitter/Emitter Head Quad.asset";
        static string IncludePath(string folder) => $"{folder}/Emitter/ShaderBridge.cginc";
        static string AvatarFolder(string folder, GameObject avatar) => $"{folder}/Avatars/{Sanitize(avatar.name)}";

        public static string DefaultShaderName(BridgeSettings s) => $"{s.name}/Emitter";

        static readonly Regex SafeName = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 _\-\(\)\.]*$");
        static readonly Regex SafeParameter = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 _\-\(\)\./]*$");

        public static string Validate(BridgeSettings s)
        {
            if (string.IsNullOrWhiteSpace(s.name)) return "Enter a name.";
            if (s.name != s.name.Trim() || !SafeName.IsMatch(s.name)) return "The name can only use letters, digits, spaces and - _ ( ) . and must start with a letter or digit.";
            if (s.bits < 1 || s.bits > SourceBits) return $"Bits must be between 1 and {SourceBits}.";

            if (s.sync && !string.IsNullOrEmpty(s.syncParameter) && (s.syncParameter != s.syncParameter.Trim() || !SafeParameter.IsMatch(s.syncParameter)))
                return "The synced parameter can only use letters, digits, spaces and - _ ( ) . / and must start with a letter or digit.";

            return null;
        }

        public static string Instance(BridgeSettings s, string parentFolder, EmitterSource source, Shader existingShader, string shaderName)
        {
            string error = Validate(s);
            if (error != null) return Fail(error);

            parentFolder = (parentFolder ?? "").TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(parentFolder) || !(parentFolder + "/").StartsWith("Assets/")) return Fail("Pick an output folder inside Assets.");

            string package = PackageFolder;
            if (string.IsNullOrEmpty(package) || !AssetDatabase.IsValidFolder(SourceFolder)) return Fail("The package's files are missing, reinstall the package.");

            string folder = Folder(parentFolder, s);
            if ((folder + "/").StartsWith(package + "/")) return Fail("Pick an output folder outside the package.");
            if (string.IsNullOrWhiteSpace(shaderName)) shaderName = DefaultShaderName(s);
            if (shaderName.Contains("\"")) return Fail("The shader name can't contain quotes.");

            var warnings = new List<string>();
            var copies = CopySource(SourceFolder, folder, s);

            WriteText(IncludePath(folder), File.ReadAllText(AssetDatabase.GUIDToAssetPath(IncludeGuid)));
            Shader shader = existingShader;

            if (source != EmitterSource.ExistingShader)
            {
                string shaderPath = EmitterShaderPath(folder, s);
                if (!File.Exists(shaderPath))
                {
                    string template = File.ReadAllText(AssetDatabase.GUIDToAssetPath(source == EmitterSource.LightLevelExample ? LightLevelGuid : TemplateGuid));
                    WriteText(shaderPath, new Regex("Shader\\s*\"[^\"]*\"").Replace(template, $"Shader \"{shaderName}\"", 1));
                }
                shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            }

            if (shader == null) return Fail("Pick the emitter shader.");
            if (shader.FindPropertyIndex("_BurnBudget") < 0 || shader.FindPropertyIndex("_BridgeBits") < 0)
                warnings.Add($"'{shader.name}' doesn't declare the bridge properties, it doesn't look like an emitter shader.");
            if (ShaderUtil.ShaderHasError(shader)) warnings.Add($"'{shader.name}' has compile errors.");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(folder, s));
            if (mat == null) return Fail($"The source has no '{SourceName} Emitter.mat'.");

            mat.shader = shader;
            mat.SetFloat("_BridgeBits", s.bits);
            EditorUtility.SetDirty(mat);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath(folder, s));
            if (controller == null) return Fail($"The source has no '{SourceName} FX.controller'.");

            Trim(controller, s);
            EditorUtility.SetDirty(controller);
            TrimParameters(folder, s);
            AssetDatabase.SaveAssets();

            var used = new HashSet<string>(AssetDatabase.GetDependencies(ControllerPath(folder, s), true));
            foreach (var clip in copies.Where(p => p.EndsWith(".anim") && !used.Contains(p))) AssetDatabase.DeleteAsset(clip);

            int empty = used.Count(p => p.EndsWith(".anim") && AnimationUtility.GetCurveBindings(AssetDatabase.LoadAssetAtPath<AnimationClip>(p)).Length == 0);
            if (empty > 0) return Fail($"{empty} clips of the instance are empty.");

            var references = PackageReferences(folder);
            if (references.Count > 0) warnings.Add("These files still come from the package, the folder won't work without it: " + string.Join(", ", references));

            string result = $"Instanced '{folder}' ({s.bits} bit{(s.bits == 1 ? "" : "s")}, output '{s.ParameterPrefix}Value'{(s.sync ? $", synced as '{s.SyncParameter}'" : "")}).";
            return Finish(result, warnings);
        }

        static List<string> CopySource(string source, string target, BridgeSettings s)
        {
            string sync = s.sync ? s.SyncParameter : s.ParameterPrefix + "Synced";
            var names = new Regex(Regex.Escape(SourceSync) + "|" + Regex.Escape(SourcePrefix) + "|" + Regex.Escape(SourceName));
            string Rename(string text) => names.Replace(text, m => m.Value == SourceSync ? sync : m.Value == SourcePrefix ? s.ParameterPrefix : s.name);

            string TargetPath(string file)
            {
                string relative = file.Substring(source.Length);
                string name = Path.GetFileName(relative);
                if (name.StartsWith(SourceName)) name = s.name + name.Substring(SourceName.Length);
                return target + Path.GetDirectoryName(relative).Replace('\\', '/').TrimEnd('/') + "/" + name;
            }

            var shaders = new HashSet<string> { IncludeGuid, LightLevelGuid, TemplateGuid };
            var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories).Select(f => f.Replace('\\', '/'))
                .Where(f => !f.EndsWith(".meta") && File.Exists(f + ".meta") && !shaders.Contains(MetaGuid(f + ".meta"))).ToList();
            var guids = new Dictionary<string, string>();

            foreach (var file in files)
            {
                string copy = TargetPath(file);
                guids[MetaGuid(file + ".meta")] = File.Exists(copy + ".meta") ? MetaGuid(copy + ".meta") : System.Guid.NewGuid().ToString("N");
            }

            var copies = new List<string>();
            var guidReference = new Regex("guid: ([0-9a-f]{32})");

            foreach (var file in files)
            {
                string copy = TargetPath(file);
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                string text = guidReference.Replace(File.ReadAllText(file), m => guids.TryGetValue(m.Groups[1].Value, out var g) ? "guid: " + g : m.Value);
                File.WriteAllText(copy, Rename(text));

                if (!File.Exists(copy + ".meta"))
                {
                    string sourceGuid = MetaGuid(file + ".meta");
                    File.WriteAllText(copy + ".meta", File.ReadAllText(file + ".meta").Replace(sourceGuid, guids[sourceGuid]));
                }

                copies.Add(copy);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return copies;
        }

        static void Trim(AnimatorController c, BridgeSettings s)
        {
            string P = s.ParameterPrefix, L = s.LayerPrefix;
            int n = s.bits;

            for (int i = c.layers.Length - 1; i >= 0; i--)
            {
                string layer = c.layers[i].name;
                if (Enumerable.Range(n, SourceBits - n).Any(k => layer == $"{L}Channel {k}") || (!s.sync && layer == L + "Sync")
                    || layer == L + (s.stableOutput ? "Raw Output" : "Stable Output"))
                    c.RemoveLayer(i);
            }

            var parameters = c.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == P + "Bits") parameters[i].defaultFloat = n;
                if (parameters[i].name == P + "Norm") parameters[i].defaultFloat = 1f / ((1 << n) - 1);
                if (parameters[i].name == s.SyncParameter && s.sync) parameters[i].type = n == 1 ? AnimatorControllerParameterType.Bool : AnimatorControllerParameterType.Int;
            }

            c.parameters = parameters;
            bool Unused(string name) =>
                Enumerable.Range(n, SourceBits - n).Any(k => name == $"{P}Bit{k}" || name == $"{P}Sum{k}" || name == $"{P}Phase{k}" || name == $"{P}Busy{k}")
                || (!s.sync && name == P + "Synced") || (!s.stableOutput && (name == P + "PassLevel" || name == P + "PassDiff"));

            foreach (var p in c.parameters.Where(p => Unused(p.name)).ToList()) c.RemoveParameter(p);

            foreach (var layer in c.layers)
            {
                var sm = layer.stateMachine;
                if (layer.name == L + "Scheduler")
                {
                    foreach (var st in sm.states.Select(x => x.state).Where(x => Enumerable.Range(n, SourceBits - n).Any(k => x.name.StartsWith($"Slot {k} "))).ToList())
                        sm.RemoveState(st);

                    if (!s.localOnly)
                        foreach (var t in sm.states.Select(x => x.state).First(x => x.name == "Warmup").transitions)
                            t.conditions = t.conditions.Where(x => x.parameter != "IsLocal").ToArray();
                }

                if (layer.name == L + "Sync")
                {
                    foreach (var t in sm.anyStateTransitions.Where(t => LevelOf(t.destinationState) >= 1 << n).ToList()) sm.RemoveAnyStateTransition(t);
                    foreach (var st in sm.states.Select(x => x.state).Where(x => LevelOf(x) >= 1 << n).ToList()) sm.RemoveState(st);
                }

                if (layer.name == L + "Decode" && sm.defaultState != null && sm.defaultState.motion is BlendTree decode)
                {
                    var removed = decode.children.Where(ch => Unused(ch.directBlendParameter) || (ch.motion is BlendTree bt && Unused(bt.blendParameter))).ToList();
                    decode.children = decode.children.Except(removed).ToArray();

                    foreach (var ch in removed)
                        if (ch.motion is BlendTree bt && AssetDatabase.IsSubAsset(bt)) Object.DestroyImmediate(bt, true);
                }
            }
        }

        static int LevelOf(AnimatorState st) => st != null && st.name.StartsWith("Level ") && int.TryParse(st.name.Substring(6), out int q) ? q : -1;

        static void TrimParameters(string folder, BridgeSettings s)
        {
            string path = ParametersPath(folder, s);
            if (!s.sync)
            {
                AssetDatabase.DeleteAsset(path);
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(path);
            if (asset == null) return;

            foreach (var p in asset.parameters.Where(p => p.name == s.SyncParameter))
                p.valueType = s.bits == 1 ? VRCExpressionParameters.ValueType.Bool : VRCExpressionParameters.ValueType.Int;

            EditorUtility.SetDirty(asset);
        }

        public static string Install(VRCAvatarDescriptor descriptor, string folder, BridgeSettings s)
        {
            if (descriptor == null) return Fail("Pick an avatar (an object with a VRC Avatar Descriptor).");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(folder, s));
            var source = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath(folder, s));
            if (prefab == null || source == null) return Fail($"Nothing instanced at '{folder}' yet.");

            var root = descriptor.gameObject;
            if (EditorUtility.IsPersistent(root)) return Fail("Install on an avatar in the scene, not on a prefab asset.");

            var warnings = new List<string>();

            RemoveObject(root, s);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            instance.name = s.name;
            Undo.RegisterCreatedObjectUndo(instance, "Install " + s.name);
            var smr = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var animator = root.GetComponent<Animator>();
            var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;

            if (head != null)
            {
                float scale = root.transform.lossyScale.x / Mathf.Max(1e-6f, head.lossyScale.x);
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(HeadMeshPath(folder));
                if (scale < 0.67f || scale > 1.5f) mesh = ScaledCopy(mesh, scale, $"{AvatarFolder(folder, root)}/Emitter Head Quad.asset");

                smr.sharedMesh = mesh;
                smr.bones = new[] { head };
                smr.rootBone = head;
                smr.localBounds = mesh.bounds;
            }
            else warnings.Add("The avatar isn't humanoid, so the emitter covers the space above the avatar root instead of following the head.");

            smr.probeAnchor = ProbeAnchor(root, instance.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(smr);

            var fx = GetOrCreateFX(descriptor, AvatarFolder(folder, root));
            if (fx == source) return Fail("The avatar uses the instance's own FX controller, give it its own FX controller.");

            ControllerMerger.Remove(fx, s, LayerNames(s));
            ControllerMerger.CopyLayers(source, fx);
            EditorUtility.SetDirty(fx);

            if (s.sync)
            {
                var expressions = AddSyncedParameter(descriptor, AvatarFolder(folder, root), s);
                if (expressions.CalcTotalCost() > VRCExpressionParameters.MAX_PARAMETER_COST)
                    warnings.Add($"The avatar now uses {expressions.CalcTotalCost()} of {VRCExpressionParameters.MAX_PARAMETER_COST} synced parameter bits.");
            }

            EditorUtility.SetDirty(descriptor);
            AssetDatabase.SaveAssets();

            var others = OtherBridges(descriptor, s);
            if (others.Count > 0) warnings.Add("Other bridge emitters on this avatar: " + string.Join(", ", others) + ". Bridges see each other's shader burns as noise.");
            return Finish($"Installed '{s.name}' on {root.name}, FX: {AssetDatabase.GetAssetPath(fx)}.", warnings);
        }

        public static string Uninstall(VRCAvatarDescriptor descriptor, BridgeSettings s)
        {
            if (descriptor == null) return Fail("Pick an avatar.");

            RemoveObject(descriptor.gameObject, s);

            foreach (var layer in descriptor.baseAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0])
            {
                if (layer.type != VRCAvatarDescriptor.AnimLayerType.FX || !(layer.animatorController is AnimatorController c)) continue;
                ControllerMerger.Remove(c, s, LayerNames(s));
                EditorUtility.SetDirty(c);
            }

            var expressions = descriptor.expressionParameters;
            if (expressions != null && s.sync && expressions.parameters != null)
            {
                expressions.parameters = expressions.parameters.Where(p => p == null || p.name != s.SyncParameter).ToArray();
                EditorUtility.SetDirty(expressions);
            }

            AssetDatabase.SaveAssets();
            return $"Removed '{s.name}' from {descriptor.gameObject.name}.";
        }

        static HashSet<string> LayerNames(BridgeSettings s)
        {
            var source = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{SourceFolder}/{SourceName} FX.controller");
            var names = new HashSet<string>();
            if (source != null)
                foreach (var layer in source.layers)
                    names.Add(layer.name.StartsWith(SourceName + " ") ? s.LayerPrefix + layer.name.Substring(SourceName.Length + 1) : layer.name);

            return names;
        }

        public static List<string> PackageReferences(string folder)
        {
            string package = PackageFolder + "/";
            var files = AssetDatabase.FindAssets("", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !AssetDatabase.IsValidFolder(p)).Distinct().ToArray();

            return AssetDatabase.GetDependencies(files, true).Where(p => p.StartsWith(package)).Distinct().ToList();
        }

        public static List<string> OtherBridges(VRCAvatarDescriptor descriptor, BridgeSettings s)
        {
            var own = descriptor.transform.Find(s.name);

            return descriptor.GetComponentsInChildren<Renderer>(true)
                .Where(r => (own == null || !r.transform.IsChildOf(own)) && r.sharedMaterials.Any(m => m != null && m.HasProperty("_BurnBudget") && m.HasProperty("_Armed0")))
                .Select(r => AnimationUtility.CalculateTransformPath(r.transform, descriptor.transform)).ToList();
        }

        static Mesh ScaledCopy(Mesh mesh, float scale, string path)
        {
            var copy = Object.Instantiate(mesh);
            copy.name = Path.GetFileNameWithoutExtension(path);
            copy.vertices = mesh.vertices.Select(v => v * scale).ToArray();
            copy.RecalculateBounds();

            EnsureFolder(Parent(path));
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(copy, path);

            return copy;
        }

        static VRCExpressionParameters AddSyncedParameter(VRCAvatarDescriptor d, string avatarFolder, BridgeSettings s)
        {
            var expressions = d.expressionParameters;
            if (expressions == null)
            {
                EnsureFolder(avatarFolder);
                expressions = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                expressions.parameters = new VRCExpressionParameters.Parameter[0];
                AssetDatabase.CreateAsset(expressions, AssetDatabase.GenerateUniqueAssetPath($"{avatarFolder}/Expression Parameters.asset"));
                d.customExpressions = true;
                d.expressionParameters = expressions;
            }

            var list = new List<VRCExpressionParameters.Parameter>(expressions.parameters ?? new VRCExpressionParameters.Parameter[0]);
            list.RemoveAll(p => p != null && p.name == s.SyncParameter);
            list.Add(new VRCExpressionParameters.Parameter
            {
                name = s.SyncParameter,
                valueType = s.bits == 1 ? VRCExpressionParameters.ValueType.Bool : VRCExpressionParameters.ValueType.Int,
                defaultValue = 0,
                saved = false,
                networkSynced = true,
            });

            expressions.parameters = list.ToArray();
            EditorUtility.SetDirty(expressions);

            return expressions;
        }

        static AnimatorController GetOrCreateFX(VRCAvatarDescriptor d, string avatarFolder)
        {
            d.customizeAnimationLayers = true;
            if (d.baseAnimationLayers == null || d.baseAnimationLayers.Length == 0)
            {
                var types = new[] { VRCAvatarDescriptor.AnimLayerType.Base, VRCAvatarDescriptor.AnimLayerType.Additive, VRCAvatarDescriptor.AnimLayerType.Gesture, VRCAvatarDescriptor.AnimLayerType.Action, VRCAvatarDescriptor.AnimLayerType.FX };
                d.baseAnimationLayers = System.Array.ConvertAll(types, t => new VRCAvatarDescriptor.CustomAnimLayer { type = t, isDefault = true, isEnabled = true });
            }

            var layers = d.baseAnimationLayers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].type != VRCAvatarDescriptor.AnimLayerType.FX) continue;
                if (layers[i].animatorController is AnimatorController existing && !layers[i].isDefault) return existing;

                EnsureFolder(avatarFolder);
                var created = AnimatorController.CreateAnimatorControllerAtPath(AssetDatabase.GenerateUniqueAssetPath($"{avatarFolder}/FX.controller"));
                layers[i].animatorController = created;
                layers[i].isDefault = false;
                d.baseAnimationLayers = layers;

                return created;
            }
            throw new System.Exception("The avatar descriptor has no FX layer slot.");
        }

        static Transform ProbeAnchor(GameObject root, Transform exclude)
        {
            var counts = new Dictionary<Transform, int>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.probeAnchor == null || r.transform.IsChildOf(exclude)) continue;
                counts.TryGetValue(r.probeAnchor, out int n);
                counts[r.probeAnchor] = n + 1;
            }

            Transform best = null;
            foreach (var kv in counts)
                if (best == null || kv.Value > counts[best]) best = kv.Key;

            return best;
        }

        static void RemoveObject(GameObject root, BridgeSettings s)
        {
            var existing = root.transform.Find(s.name);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        }

        static string MetaGuid(string metaPath)
        {
            foreach (string line in File.ReadLines(metaPath))
                if (line.StartsWith("guid:")) return line.Substring(5).Trim();

            throw new System.Exception("No GUID in " + metaPath);
        }

        static void WriteText(string path, string text)
        {
            if (File.Exists(path) && File.ReadAllText(path) == text) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Parent(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string Parent(string path) => string.IsNullOrEmpty(path) ? "" : (Path.GetDirectoryName(path) ?? "").Replace('\\', '/');

        static string Sanitize(string s)
        {
            foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');

            return s;
        }

        static string Finish(string result, List<string> warnings)
        {
            foreach (var w in warnings) Debug.LogWarning("[Shader To Animator Bridge] " + w);
            Debug.Log("[Shader To Animator Bridge] " + result);

            return warnings.Count == 0 ? result : result + "\nWARNING: " + string.Join("\nWARNING: ", warnings);
        }

        static string Fail(string message)
        {
            Debug.LogWarning("[Shader To Animator Bridge] " + message);

            return "ERROR: " + message;
        }
    }
}
