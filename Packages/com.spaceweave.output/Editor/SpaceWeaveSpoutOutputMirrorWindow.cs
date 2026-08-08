using SpaceWeave.Output;
using UnityEditor;
using UnityEngine;

namespace SpaceWeave.Output.Editor
{
    public sealed class SpaceWeaveSpoutOutputMirrorWindow : EditorWindow
    {
        SpaceWeaveOutputManager _manager;

        [MenuItem("Window/SpaceWeave/Spout Output Mirror")]
        public static void Open()
        {
            var window = GetWindow<SpaceWeaveSpoutOutputMirrorWindow>();
            window.titleContent = new GUIContent("Spout Output Mirror");
            window.minSize = new Vector2(520, 320);
            window.Show();
        }

        void OnEnable()
        {
            EditorApplication.update += Repaint;
            FindManager();
        }

        void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        void FindManager()
        {
            if (_manager == null)
                _manager = Object.FindObjectOfType<SpaceWeaveOutputManager>(true);
        }

        void OnGUI()
        {
            FindManager();
            EditorGUILayout.LabelField(
                "THIS IS THE EXACT TEXTURE SENT TO SPOUT",
                new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 15
                });

            if (_manager == null)
            {
                EditorGUILayout.HelpBox(
                    "No SpaceWeaveOutputManager exists in the loaded scene.",
                    MessageType.Warning);
                if (GUILayout.Button("Refresh")) FindManager();
                return;
            }

            RenderTexture texture = _manager.FinalSenderTexture;
            EditorGUILayout.LabelField(
                $"{_manager.mode} | " +
                (texture != null
                    ? $"{texture.name} {texture.width}x{texture.height} " +
                      $"{texture.graphicsFormat} sRGB={texture.sRGB}"
                    : "final texture not created"));
            EditorGUILayout.LabelField(
                $"Sender: {_manager.SenderName} | exact source: {_manager.SpoutSourceMatchesFinal}");

            Rect preview = GUILayoutUtility.GetAspectRect(
                texture != null && texture.height > 0
                    ? (float)texture.width / texture.height
                    : 2f,
                GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(preview, Color.black);
            if (texture != null)
                EditorGUI.DrawPreviewTexture(preview, texture, null, ScaleMode.ScaleToFit);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save Final Sender RT to PNG"))
                    _manager.SaveFinalSenderCapture();
                if (GUILayout.Button("Validate Pixels"))
                    _manager.ValidateFinalSenderTexture();
                if (GUILayout.Button("Resume Animated Pattern"))
                    _manager.ResumeAnimatedTruthPattern();
            }

            SpaceWeaveFinalTextureValidationResult validation = _manager.LastValidation;
            if (validation != null)
                EditorGUILayout.HelpBox(
                    $"{validation.summary}\nSHA-256: {validation.canonicalRgbSha256}",
                    validation.passed ? MessageType.Info : MessageType.Error);
            if (!string.IsNullOrEmpty(_manager.LastCapturePath))
                EditorGUILayout.SelectableLabel(
                    _manager.LastCapturePath,
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }
    }

    [CustomEditor(typeof(SpaceWeaveOutputManager))]
    public sealed class SpaceWeaveOutputManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var manager = (SpaceWeaveOutputManager)target;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                $"Spout / NDI name: {manager.SenderName}\n" +
                $"SpaceWeave input format: {SpaceWeaveOutputContract.SpaceWeaveInputFormatHint(manager.mode)}",
                MessageType.None);

            if (Application.isPlaying && !manager.SpoutSourceMatchesFinal)
                EditorGUILayout.HelpBox(
                    "Spout Sender is not sending FinalSpaceWeaveSenderRT. " +
                    "The manager will correct this binding.",
                    MessageType.Error);
            else if (Application.isPlaying)
                EditorGUILayout.HelpBox(
                    "Spout Texture capture references the exact final sender RT.",
                    MessageType.Info);

            if (manager.LegacyOutputTexture != null)
                EditorGUILayout.HelpBox(
                    $"Legacy asset '{manager.LegacyOutputTexture.name}' is migration evidence only " +
                    "and is not sent.",
                    MessageType.Warning);

            if (GUILayout.Button("Open Spout Output Mirror"))
                SpaceWeaveSpoutOutputMirrorWindow.Open();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save Final Sender RT to PNG"))
                    manager.SaveFinalSenderCapture();
                if (GUILayout.Button("Resume Animated Pattern"))
                    manager.ResumeAnimatedTruthPattern();
            }
        }
    }

    /// <summary>
    /// Builds a playable sample scene with camera + manager + Spout (+ optional NDI).
    /// </summary>
    public static class SpaceWeaveSampleSceneMenu
    {
        const string SampleDir = "Assets/Samples/SpaceWeave Output/0.1.0/Output Validation";

        [MenuItem("SpaceWeave/Create Sample Scene")]
        public static void CreateSampleScene()
        {
            if (!EditorUtility.DisplayDialog(
                    "SpaceWeave Sample Scene",
                    "Create a new scene with SpaceWeaveOutputManager + SpoutSender " +
                    "(and optional NDI)? Unsaved scene changes will prompt to save.",
                    "Create", "Cancel"))
                return;

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            var camGo = GameObject.Find("Main Camera");
            if (camGo == null)
            {
                camGo = new GameObject("Main Camera");
                camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                camGo.tag = "MainCamera";
            }

            camGo.transform.position = new Vector3(0f, 1.6f, 0f);
            var cam = camGo.GetComponent<Camera>();

            var manager = camGo.GetComponent<SpaceWeaveOutputManager>();
            if (manager == null) manager = camGo.AddComponent<SpaceWeaveOutputManager>();
            manager.sourceCamera = cam;
            manager.mode = SpaceWeaveOutputMode.Equirectangular;
            manager.senderBaseName = "SpaceWeave";
            manager.panoramaWidth = 2048;
            manager.cubemapFaceSize = 512;

            var spout = camGo.GetComponent<Klak.Spout.SpoutSender>();
            if (spout == null) spout = camGo.AddComponent<Klak.Spout.SpoutSender>();
            spout.captureMethod = Klak.Spout.CaptureMethod.Texture;
            manager.spoutSender = spout;

            // NDI present but disabled — enable in Inspector if needed.
            var ndi = camGo.GetComponent<Klak.Ndi.NdiSender>();
            if (ndi == null) ndi = camGo.AddComponent<Klak.Ndi.NdiSender>();
            ndi.enabled = false;
            manager.ndiSender = ndi;

            var rigGo = new GameObject("SpaceWeave Diagnostic Rig");
            rigGo.AddComponent<SpaceWeaveDiagnosticRig>();

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);

            if (!AssetDatabase.IsValidFolder("Assets/Samples"))
                AssetDatabase.CreateFolder("Assets", "Samples");
            // Prefer saving next to imported sample if present; else Assets/SpaceWeaveSample.
            string savePath = "Assets/SpaceWeave_Sample.unity";
            if (AssetDatabase.IsValidFolder(SampleDir + "/Scenes"))
                savePath = SampleDir + "/Scenes/SpaceWeave_Sample.unity";

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, savePath);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(
                "SpaceWeave Sample",
                $"Saved {savePath}\n\nPlay, then in SpaceWeave select Spout sender " +
                "\"SpaceWeave_EQUIRECT\" and set Input format to Equirect.",
                "OK");
        }
    }
}
