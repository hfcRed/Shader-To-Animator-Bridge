using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace hfcRed.ShaderToAnimatorBridge
{
    public class BridgeWindow : EditorWindow
    {
        const string PrefsKey = "hfcRed.ShaderToAnimatorBridge.Window";

        [System.Serializable]
        class State
        {
            public BridgeSettings settings = new BridgeSettings();
            public EmitterSource source = EmitterSource.Template;
            public string shaderName = "";
            public string existingShaderGuid = "";
            public string outputFolder = "Assets";
        }

        static readonly GUIContent[] SourceNames =
        {
            new GUIContent("Light Level (Example)", "Sends how brightly the avatar is lit."),
            new GUIContent("Template (Value Slider)", "Sends a material slider. Start here and replace BridgeValue() with your own logic."),
            new GUIContent("Existing Shader", "An emitter shader you already wrote."),
        };

        State state;
        Shader existingShader;
        VRCAvatarDescriptor avatar;
        Vector2 scroll;
        string message;

        [MenuItem("hfcRed/Tools/Shader To Animator Bridge")]
        static void Open()
        {
            var window = GetWindow<BridgeWindow>("Shader Bridge");
            window.minSize = new Vector2(360, 420);
        }

        void OnEnable()
        {
            state = new State();
            string json = EditorPrefs.GetString(PrefsKey, "");
            if (json != "") JsonUtility.FromJsonOverwrite(json, state);
            if (state.existingShaderGuid != "") existingShader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(state.existingShaderGuid));
            if (avatar == null && Selection.activeGameObject != null) avatar = Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>();
        }

        void OnDisable() => EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(state));

        void OnGUI()
        {
            var s = state.settings;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("Creates a Shader To Animator Bridge (STAB) instance. "
                + "The contents of the instanced folder is what you would bundle with a package.", MessageType.None);

            Header("System");
            s.name = EditorGUILayout.TextField(new GUIContent("Name", "Names the folder, the object at the avatar root, the layers, and the parameters."), s.name);
            s.bits = EditorGUILayout.IntSlider(new GUIContent("Bits", "The value is sent as one of 2^bits levels. Each bit adds one query slot. More bits update slower."), s.bits, 1, 8);
            EditorGUILayout.LabelField(" ", (s.bits == 1 ? "On/off flag" : $"{1 << s.bits} levels") + $", read from '{s.ParameterPrefix}Value'", EditorStyles.miniLabel);
            s.sync = EditorGUILayout.Toggle(new GUIContent("Sync To Others", "Mirror the output value to a synced parameter (Bool for 1 bit, Int otherwise)."), s.sync);
            if (s.sync)
            {
                s.syncParameter = EditorGUILayout.TextField(new GUIContent("Synced Parameter", "Leave empty for '" + s.ParameterPrefix + "Synced'."), s.syncParameter);
                if (string.IsNullOrEmpty(s.syncParameter)) EditorGUILayout.LabelField(" ", s.SyncParameter, EditorStyles.miniLabel);
            }

            s.localOnly = EditorGUILayout.Toggle(new GUIContent("Wearer Only", "Only the wearer runs the bridge. Turn off to let every viewer run the bridge on their own GPU. Generally not recommended."), s.localOnly);
            s.stableOutput = EditorGUILayout.Toggle(new GUIContent("Stable Output", "Update the output only after it has been confirmed by a second pass. Without it you might see in-between levels while bits are flipping or see momentary false flips, but the output will update faster."), s.stableOutput);
            if (s.stableOutput && s.bits == 1) EditorGUILayout.LabelField(" ", "A bool has no in-between levels so this only hides false flips at the cost of doubling the latency.", EditorStyles.miniLabel);

            Header("Emitter Shader");
            state.source = (EmitterSource)EditorGUILayout.Popup(new GUIContent("Shader"), (int)state.source, SourceNames);
            string folder = BridgeInstancer.Folder(state.outputFolder.TrimEnd('/'), s);
            if (state.source == EmitterSource.ExistingShader)
            {
                existingShader = (Shader)EditorGUILayout.ObjectField("Emitter Shader", existingShader, typeof(Shader), false);
                state.existingShaderGuid = existingShader != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(existingShader)) : "";
            }
            else if (File.Exists(BridgeInstancer.EmitterShaderPath(folder, s)))
                EditorGUILayout.HelpBox($"Keeps the emitter shader already in the output folder. Delete '{s.name} Emitter.shader' to start over from the template.", MessageType.None);
            else
            {
                state.shaderName = EditorGUILayout.TextField(new GUIContent("Shader Name", "Name of the emitter shader made from the template, as shown in the material's shader menu."), state.shaderName);
                if (string.IsNullOrWhiteSpace(state.shaderName)) EditorGUILayout.LabelField(" ", BridgeInstancer.DefaultShaderName(s), EditorStyles.miniLabel);
            }

            Header("Output");
            using (new EditorGUILayout.HorizontalScope())
            {
                state.outputFolder = EditorGUILayout.TextField("Folder", state.outputFolder);
                if (GUILayout.Button("Browse", GUILayout.Width(64))) BrowseFolder();
            }
            EditorGUILayout.LabelField(" ", folder, EditorStyles.miniLabel);

            Header("Avatar");
            avatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(new GUIContent("Avatar", "Install the instance on this avatar."), avatar, typeof(VRCAvatarDescriptor), true);
            if (avatar != null)
            {
                var others = BridgeInstancer.OtherBridges(avatar, s);
                if (others.Count > 0)
                    EditorGUILayout.HelpBox("This avatar already has another bridge: " + string.Join(", ", others) + ". Two bridges see each other's burns as noise, combine them into one bridge with more bits instead.", MessageType.Warning);
            }

            EditorGUILayout.Space();
            string error = BridgeInstancer.Validate(s);
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);

            using (new EditorGUI.DisabledScope(error != null))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Instance", GUILayout.Height(28))) Run(false);
                using (new EditorGUI.DisabledScope(avatar == null))
                    if (GUILayout.Button("Create And Install", GUILayout.Height(28))) Run(true);
            }
            using (new EditorGUI.DisabledScope(avatar == null || error != null))
                if (GUILayout.Button("Remove From Avatar")
                    && EditorUtility.DisplayDialog("Remove", $"Remove '{s.name}' from {avatar.gameObject.name}? The instance folder is kept.", "Remove", "Cancel"))
                    message = BridgeInstancer.Uninstall(avatar, s);

            if (!string.IsNullOrEmpty(message))
                EditorGUILayout.HelpBox(message, message.StartsWith("ERROR") ? MessageType.Error : message.Contains("WARNING") ? MessageType.Warning : MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        void Run(bool install)
        {
            var s = state.settings;
            message = BridgeInstancer.Instance(s, state.outputFolder, state.source, existingShader, state.shaderName);
            if (message.StartsWith("ERROR")) return;

            string folder = BridgeInstancer.Folder(state.outputFolder.TrimEnd('/'), s);
            if (install) message += "\n" + BridgeInstancer.Install(avatar, folder, s);

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(folder));
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(state));
        }

        void BrowseFolder()
        {
            string picked = EditorUtility.OpenFolderPanel("Output Folder", state.outputFolder, "");
            if (string.IsNullOrEmpty(picked)) return;

            string assets = Application.dataPath.Replace('\\', '/');
            picked = picked.Replace('\\', '/');
            if (picked == assets || picked.StartsWith(assets + "/")) state.outputFolder = "Assets" + picked.Substring(assets.Length);
            else message = "ERROR: Pick a folder inside the project's Assets folder.";
        }

        static void Header(string text)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }
    }
}
