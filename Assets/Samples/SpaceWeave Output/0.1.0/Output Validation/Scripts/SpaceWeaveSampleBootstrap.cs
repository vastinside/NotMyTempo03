using Klak.Ndi;
using Klak.Spout;
using SpaceWeave.Output;
using UnityEngine;

namespace SpaceWeave.Output.Samples
{
    /// <summary>
    /// Safety net for the sample scene: ensures SpaceWeaveOutputManager +
    /// SpoutSender (+ optional NDI) are present and wired on first Play.
    /// The shipped sample already serializes those components; this fills gaps
    /// if an import stripped script references.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class SpaceWeaveSampleBootstrap : MonoBehaviour
    {
        [SerializeField] SpaceWeaveOutputMode mode = SpaceWeaveOutputMode.Equirectangular;
        [SerializeField] string senderBaseName = "SpaceWeave";
        [SerializeField] bool enableNdi = false;
        [SerializeField] bool addDiagnosticRig = true;
        [SerializeField] bool addGroundPlane = true;

        void Awake()
        {
            var cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
                go.tag = "MainCamera";
                go.transform.position = new Vector3(0f, 1.6f, 0f);
            }

            var manager = cam.GetComponent<SpaceWeaveOutputManager>();
            if (manager == null) manager = cam.gameObject.AddComponent<SpaceWeaveOutputManager>();
            manager.sourceCamera = cam;
            manager.mode = mode;
            manager.senderBaseName = senderBaseName;
            if (manager.panoramaWidth > 4096) manager.panoramaWidth = 2048;

            var spout = cam.GetComponent<SpoutSender>();
            if (spout == null) spout = cam.gameObject.AddComponent<SpoutSender>();
            spout.captureMethod = Klak.Spout.CaptureMethod.Texture;
            manager.spoutSender = spout;

            var ndi = cam.GetComponent<NdiSender>();
            if (ndi == null) ndi = cam.gameObject.AddComponent<NdiSender>();
            ndi.enabled = enableNdi;
            manager.ndiSender = ndi;

            if (addDiagnosticRig && FindObjectOfType<SpaceWeaveDiagnosticRig>() == null)
            {
                var rig = new GameObject("SpaceWeave Diagnostic Rig");
                rig.AddComponent<SpaceWeaveDiagnosticRig>();
            }

            if (addGroundPlane && GameObject.Find("Ground") == null)
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.localScale = new Vector3(2f, 1f, 2f);
            }

            Debug.Log(
                $"[SpaceWeave Sample] Ready. Spout=\"{manager.SenderName}\" → " +
                $"SpaceWeave input format: {SpaceWeaveOutputContract.SpaceWeaveInputFormatHint(manager.mode)}",
                manager);
        }
    }
}
