using System;
using System.IO;
using Klak.Ndi;
using Klak.Spout;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace SpaceWeave.Output
{

[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public class SpaceWeaveOutputManager : MonoBehaviour
{
    public enum OutputContent
    {
        Normal,
        SpaceWeaveSourceTruthPattern
    }

    [Header("Source Contract")]
    public SpaceWeaveOutputMode mode = SpaceWeaveOutputMode.Equirectangular;
    public OutputContent outputContent = OutputContent.Normal;
    public Camera sourceCamera;
    [Tooltip("Bake Camera_360 look into equirect/cylindrical via _Rotation (same as CubemapRenderer). " +
             "RenderToCubemap is world-axis aligned and ignores camera rotation — without this, " +
             "mouse/controller look works in Unity Game view but Spout/SpaceWeave stays fixed.")]
    public bool applyViewerRotation = true;
    [SerializeField, FormerlySerializedAs("outputRT"), Tooltip(
        "Migration evidence only. This asset is never sent to Spout.")]
    RenderTexture legacyOutputRT;
    [Min(2)] public int panoramaWidth = 4096;
    [Min(16)] public int cubemapFaceSize = 1024;
    [FormerlySerializedAs("hFOV")]
    [Range(30f, 360f)] public float cylindricalHFOV = 360f;
    [FormerlySerializedAs("vFOV")]
    [Range(30f, 179f)] public float cylindricalVFOV = 120f;

    [Header("Frame and Colour")]
    [Range(1, 240)] public int targetFPS = 60;
    [Min(0f)] public float exposure = 1f;
    [Range(1f, 2.4f)] public float outputGamma = 2.2f;
    public bool diagnosticOverlay;
    public bool testPatternMode;
    public bool showRuntimeStatus = true;

    [Header("GPU Conversion")]
    public Shader sh_Equirect;
    public Shader sh_Cylindrical;
    public Shader sh_CubemapPack;
    public Shader sh_Fisheye;
    public Shader sh_Eac;
    public Shader sh_SourceTruth;

    [Header("Senders")]
    public string senderBaseName = "SpaceWeave";
    public SpoutSender spoutSender;
    public NdiSender ndiSender;

    const string FinalTextureName = "FinalSpaceWeaveSenderRT";
    static readonly GraphicsFormat FinalGraphicsFormat = GraphicsFormat.R8G8B8A8_SRGB;

    RenderTexture _finalSenderRT;
    RenderTexture _cubemapRT;
    Material _equirectMaterial;
    Material _cylindricalMaterial;
    Material _packMaterial;
    Material _fisheyeMaterial;
    Material _eacMaterial;
    Material _truthMaterial;
    Texture2D _fallbackTexture;
    string _fallbackKey;
    int _builtFaceSize;
    string _warning;
    string _lastShaderFailure;
    bool _truthPatternFrozen;
    bool _lastFrameFullyRendered;
    bool _lastFallbackRendered;
    bool _lastUnexpectedMagenta;
    bool _senderCorrectionLogged;
    int _textureGeneration;
    // The stream name Klak was last actually initialised with -- NOT simply the
    // last value assigned to ndiName/spoutName, which Klak ignores after startup.
    // Drives the restart in ApplySenderContract.
    string _appliedSenderName;
    string _lastCapturePath;
    SpaceWeaveFinalTextureValidationResult _lastValidation;
    Quaternion _viewerRotation = Quaternion.identity;

    public Vector2Int RequiredOutputSize =>
        SpaceWeaveOutputContract.RequiredSize(mode, panoramaWidth, cubemapFaceSize);

    public string SenderName =>
        senderBaseName + "_" + SpaceWeaveOutputContract.SenderSuffix(mode);

    public RenderTexture FinalSenderTexture
    {
        get
        {
            if (_finalSenderRT == null || !_finalSenderRT.IsCreated())
                EnsureFinalSenderTexture();
            return _finalSenderRT;
        }
    }
    public RenderTexture LegacyOutputTexture => legacyOutputRT;
    public bool TruthPatternFrozen => _truthPatternFrozen;
    public bool LastFrameFullyRendered => _lastFrameFullyRendered;
    public bool LastFallbackRendered => _lastFallbackRendered;
    public bool LastUnexpectedMagenta => _lastUnexpectedMagenta;
    public int FinalTextureGeneration => _textureGeneration;
    public string LastCapturePath => _lastCapturePath;
    public SpaceWeaveFinalTextureValidationResult LastValidation => _lastValidation;
    public bool SpoutSourceMatchesFinal =>
        spoutSender != null &&
        spoutSender.captureMethod == Klak.Spout.CaptureMethod.Texture &&
        spoutSender.sourceTexture == _finalSenderRT;

    protected virtual void Awake()
    {
        ResolveSceneReferences();
        ResolveShaders();
        WarmConversionShaders();
        if (legacyOutputRT != null)
            Debug.LogWarning(
                $"[SpaceWeave] Legacy sender asset '{legacyOutputRT.name}' is retained for " +
                "migration evidence only. Spout will send the manager-owned runtime texture.",
                this);
        EnsureResources();
        ApplySenderContract();
    }

    void WarmConversionShaders()
    {
        // Compile blit shaders before the first LateUpdate so Spout is not pink for a beat.
        WarmShader(sh_Equirect);
        WarmShader(sh_Cylindrical);
        WarmShader(sh_CubemapPack);
        WarmShader(sh_Fisheye);
        WarmShader(sh_Eac);
        WarmShader(sh_SourceTruth);
    }

    static void WarmShader(Shader shader)
    {
        if (shader == null) return;
        var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        try { mat.SetPass(0); }
        catch { /* ignore */ }
        finally
        {
            if (Application.isPlaying) Destroy(mat);
            else DestroyImmediate(mat);
        }
    }

    protected virtual void OnEnable()
    {
        Application.targetFrameRate = targetFPS;
    }

    protected virtual void OnValidate()
    {
        panoramaWidth = Mathf.Max(2, panoramaWidth + (panoramaWidth & 1));
        cubemapFaceSize = Mathf.Max(16, cubemapFaceSize);
        cylindricalHFOV = Mathf.Clamp(cylindricalHFOV, 30f, 360f);
        cylindricalVFOV = Mathf.Clamp(cylindricalVFOV, 30f, 179f);
        targetFPS = Mathf.Clamp(targetFPS, 1, 240);
    }

    protected virtual void LateUpdate()
    {
        Application.targetFrameRate = targetFPS;
        RenderOutputFrame();
    }

    protected virtual void OnDisable() => ReleaseRuntimeResources();
    protected virtual void OnDestroy() => ReleaseRuntimeResources();

    public bool RenderOutputFrame()
    {
        ResolveSceneReferences();
        ResolveShaders();
        _lastFrameFullyRendered = false;
        _lastFallbackRendered = false;

        if (!EnsureResources()) return false;
        ApplySenderContract();
        ClearFinalTexture(Color.black);

        bool rendered;
        if (mode == SpaceWeaveOutputMode.SurfaceAtlasPlaceholder)
        {
            _warning = "Atlas/Surface Atlas is NOT implemented in Unity. Use Equirect, Cube Cross/Strip, Fisheye, or EAC.";
            ClearFinalTexture(Color.black);
            return false;
        }

        if (outputContent == OutputContent.SpaceWeaveSourceTruthPattern || testPatternMode)
        {
            rendered = RenderSourceTruthPattern();
        }
        else
        {
            if (sourceCamera == null)
            {
                _warning = "A source camera is required.";
                return false;
            }

            // RenderToCubemap uses world-axis faces from the camera position and
            // ignores orientation. Capture at identity, then apply Camera_360 look
            // through the conversion shader's _Rotation (CubemapRenderer pattern).
            _viewerRotation = sourceCamera.transform.rotation;
            Quaternion restored = _viewerRotation;
            sourceCamera.transform.rotation = Quaternion.identity;
            sourceCamera.RenderToCubemap(_cubemapRT);
            sourceCamera.transform.rotation = restored;

            switch (mode)
            {
                case SpaceWeaveOutputMode.Equirectangular:
                    rendered = RenderEquirectangular();
                    break;
                case SpaceWeaveOutputMode.Cylindrical:
                    rendered = RenderCylindrical();
                    break;
                case SpaceWeaveOutputMode.CubemapCross:
                    rendered = RenderPacked(false);
                    break;
                case SpaceWeaveOutputMode.CubemapStrip:
                    rendered = RenderPacked(true);
                    break;
                case SpaceWeaveOutputMode.Fisheye180:
                    rendered = RenderFisheye(180f);
                    break;
                case SpaceWeaveOutputMode.Fisheye360:
                    rendered = RenderFisheye(360f);
                    break;
                case SpaceWeaveOutputMode.Eac:
                    rendered = RenderEac();
                    break;
                default:
                    rendered = false;
                    break;
            }
        }

        _lastFrameFullyRendered = rendered;
        if (rendered) _warning = string.Empty;
        return rendered;
    }

    public void SetTruthPatternFrozen(bool frozen)
    {
        _truthPatternFrozen = frozen;
    }

    public void ResumeAnimatedTruthPattern()
    {
        _truthPatternFrozen = false;
    }

    public bool SaveFinalSenderCapture()
    {
        _truthPatternFrozen = true;
        if (!RenderOutputFrame() || _finalSenderRT == null)
        {
            Debug.LogError("[SpaceWeave] Final sender capture aborted: no valid rendered frame.", this);
            return false;
        }

        string root = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "SpaceWeaveValidationCaptures"));
        SpaceWeaveFinalTextureCapture capture =
            SpaceWeaveFinalTextureEvidence.CaptureAndSave(
                _finalSenderRT,
                mode,
                SenderName,
                spoutSender,
                root,
                true);
        _lastValidation = capture.validation;
        _lastUnexpectedMagenta = capture.validation.unexpectedMagenta;
        _lastCapturePath = capture.jsonPath;
        if (!capture.validation.passed)
        {
            _lastFrameFullyRendered = false;
            _warning = "Final sender pixel validation failed: " + capture.validation.summary;
            Debug.LogError("[SpaceWeave] " + _warning, this);
            return false;
        }

        Debug.Log($"[SpaceWeave] Final sender capture saved: {capture.jsonPath}", this);
        return true;
    }

    public SpaceWeaveFinalTextureValidationResult ValidateFinalSenderTexture()
    {
        if (_finalSenderRT == null || !_finalSenderRT.IsCreated())
            return SpaceWeaveFinalTextureValidationResult.Invalid(
                "Final sender texture is missing or not created.");

        _lastValidation = SpaceWeaveFinalTextureEvidence.Validate(
            _finalSenderRT,
            mode,
            outputContent == OutputContent.SpaceWeaveSourceTruthPattern || testPatternMode);
        _lastUnexpectedMagenta = _lastValidation.unexpectedMagenta;
        if (!_lastValidation.passed) _lastFrameFullyRendered = false;
        return _lastValidation;
    }

    void ResolveSceneReferences()
    {
        if (sourceCamera == null) sourceCamera = GetComponent<Camera>();
        if (spoutSender == null) spoutSender = GetComponentInParent<SpoutSender>();
        if (spoutSender == null) spoutSender = GetComponentInChildren<SpoutSender>(true);
        if (ndiSender == null) ndiSender = GetComponentInParent<NdiSender>();
        if (ndiSender == null) ndiSender = GetComponentInChildren<NdiSender>(true);
    }

    void ResolveShaders()
    {
        if (sh_Equirect == null)
            sh_Equirect = Shader.Find("SpaceWeave/CubemapToEquirect")
                ?? Shader.Find("CAVE/CubemapToEquirect");
        if (sh_Cylindrical == null)
            sh_Cylindrical = Shader.Find("SpaceWeave/CylindricalFromCubemap")
                ?? Shader.Find("CAVE/CylindricalFromCubemap");
        if (sh_CubemapPack == null) sh_CubemapPack = Shader.Find("SpaceWeave/CubemapPack");
        if (sh_Fisheye == null) sh_Fisheye = Shader.Find("SpaceWeave/FisheyeFromCubemap");
        if (sh_Eac == null) sh_Eac = Shader.Find("SpaceWeave/EacFromCubemap");
        if (sh_SourceTruth == null) sh_SourceTruth = Shader.Find("SpaceWeave/SourceTruthPattern");
    }

    bool EnsureResources()
    {
        if (!EnsureFinalSenderTexture()) return false;

        if (_cubemapRT == null || !_cubemapRT.IsCreated() || _builtFaceSize != cubemapFaceSize)
        {
            DestroyRenderTexture(ref _cubemapRT);
            _cubemapRT = new RenderTexture(cubemapFaceSize, cubemapFaceSize, 24,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "SpaceWeave_Cubemap",
                dimension = TextureDimension.Cube,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1
            };
            _cubemapRT.Create();
            _builtFaceSize = cubemapFaceSize;
        }
        return true;
    }

    bool EnsureFinalSenderTexture()
    {
        Vector2Int size = RequiredOutputSize;
        bool valid = _finalSenderRT != null &&
                     _finalSenderRT.width == size.x &&
                     _finalSenderRT.height == size.y &&
                     _finalSenderRT.graphicsFormat == FinalGraphicsFormat &&
                     _finalSenderRT.antiAliasing == 1 &&
                     !_finalSenderRT.useMipMap &&
                     !_finalSenderRT.enableRandomWrite &&
                     _finalSenderRT.IsCreated();
        if (valid) return true;

        if (spoutSender != null && spoutSender.sourceTexture == _finalSenderRT)
            spoutSender.sourceTexture = null;
        if (ndiSender != null && ndiSender.sourceTexture == _finalSenderRT)
            ndiSender.sourceTexture = null;
        DestroyRenderTexture(ref _finalSenderRT);

        var descriptor = new RenderTextureDescriptor(size.x, size.y)
        {
            graphicsFormat = FinalGraphicsFormat,
            depthBufferBits = 0,
            msaaSamples = 1,
            volumeDepth = 1,
            dimension = TextureDimension.Tex2D,
            useMipMap = false,
            autoGenerateMips = false,
            enableRandomWrite = false,
            useDynamicScale = false
        };
        _finalSenderRT = new RenderTexture(descriptor)
        {
            name = FinalTextureName,
            hideFlags = HideFlags.DontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        _finalSenderRT.Create();
        if (!_finalSenderRT.IsCreated())
        {
            _warning = "Failed to create FinalSpaceWeaveSenderRT.";
            Debug.LogError("[SpaceWeave] " + _warning, this);
            return false;
        }

        _textureGeneration++;
        ApplySenderContract();
        Debug.Log(
            $"[SpaceWeave] Created {FinalTextureName} generation {_textureGeneration}: " +
            $"{size.x}x{size.y}, {_finalSenderRT.graphicsFormat}, " +
            $"sRGB={_finalSenderRT.sRGB}, created={_finalSenderRT.IsCreated()}.",
            this);
        return true;
    }

    void ApplySenderContract()
    {
        if (_finalSenderRT == null) return;

        // Klak creates its send object exactly once and never re-reads the name:
        //
        //   NdiSender.cs:23   if (_send == null) _send = Interop.Send.Create(ndiName);
        //
        // _send is only released by Restart(), which runs from OnDisable/OnDestroy.
        // So assigning ndiName/spoutName here -- which happens on every render
        // target recreation, i.e. every output-mode change -- has NO effect on the
        // stream actually being published. captureMethod is the same story:
        // ResetState() picks the capture path on enable and is not re-run on
        // assignment. The published name therefore silently stayed whatever it was
        // at startup, so the receiver's "Unity mode=Eac sends CAVE_360_EAC" hint
        // was false and the two sides disagreed about the stream's identity.
        //
        // Restart() and ResetState() are internal to Klak, so toggling `enabled`
        // is the supported public way to force the re-initialisation.
        bool nameChanged = _appliedSenderName != SenderName;

        bool corrected = false;
        if (spoutSender != null)
        {
            corrected = spoutSender.spoutName != SenderName ||
                        spoutSender.captureMethod != Klak.Spout.CaptureMethod.Texture ||
                        spoutSender.sourceTexture != _finalSenderRT ||
                        spoutSender.keepAlpha;
            spoutSender.spoutName = SenderName;
            spoutSender.captureMethod = Klak.Spout.CaptureMethod.Texture;
            spoutSender.sourceCamera = null;
            spoutSender.sourceTexture = _finalSenderRT;
            spoutSender.keepAlpha = false;
            // Properties first, then the toggle, so the restarted capture picks up
            // the final state in one go.
            RestartSender(spoutSender, nameChanged);
        }
        if (ndiSender != null)
        {
            ndiSender.ndiName = SenderName;
            ndiSender.captureMethod = Klak.Ndi.CaptureMethod.Texture;
            ndiSender.sourceTexture = _finalSenderRT;
            ndiSender.keepAlpha = false;
            RestartSender(ndiSender, nameChanged);
        }
        if (nameChanged)
        {
            Debug.Log($"[SpaceWeave] Sender republished as \"{SenderName}\" " +
                      $"(was \"{_appliedSenderName}\"). Receivers bound to the old " +
                      "name must follow it.", this);
        }
        _appliedSenderName = SenderName;

        if (corrected && !_senderCorrectionLogged)
        {
            Debug.LogWarning(
                $"[SpaceWeave] Corrected Spout Sender to Texture capture: " +
                $"{SenderName} <- {FinalTextureName}.",
                this);
            _senderCorrectionLogged = true;
        }
        else if (!corrected)
        {
            _senderCorrectionLogged = false;
        }
    }

    // Forces Klak to tear down and rebuild its send object so a changed stream
    // name actually takes effect. Gated strictly on a real name change: toggling
    // every frame would drop the stream continuously, and a brief interruption is
    // only acceptable because the operator just deliberately changed output mode.
    //
    // Never enables a sender the operator turned off -- if it is already disabled
    // there is nothing to restart, and force-enabling it would start publishing a
    // stream they had switched off on purpose.
    static void RestartSender(Behaviour sender, bool nameChanged)
    {
        if (!nameChanged || sender == null) return;
        if (!sender.enabled) return;
        sender.enabled = false;
        sender.enabled = true;
    }

    Matrix4x4 ViewerRotationMatrix()
    {
        return applyViewerRotation
            ? Matrix4x4.Rotate(_viewerRotation)
            : Matrix4x4.identity;
    }

    bool RenderEquirectangular()
    {
        if (!EnsureMaterial(ref _equirectMaterial, sh_Equirect, "equirectangular")) return false;
        _equirectMaterial.SetTexture("_MainTex", _cubemapRT);
        _equirectMaterial.SetMatrix("_Rotation", ViewerRotationMatrix());
        _equirectMaterial.SetFloat("_YawOffsetDeg", 0f);
        _equirectMaterial.SetFloat("_PitchOffsetDeg", 0f);
        _equirectMaterial.SetFloat("_Exposure", exposure);
        _equirectMaterial.SetFloat("_Gamma", outputGamma);
        SetOutputSize(_equirectMaterial);
        Graphics.Blit(null, _finalSenderRT, _equirectMaterial);
        return true;
    }

    bool RenderCylindrical()
    {
        if (!EnsureMaterial(ref _cylindricalMaterial, sh_Cylindrical, "cylindrical")) return false;
        _cylindricalMaterial.SetTexture("_Cubemap", _cubemapRT);
        _cylindricalMaterial.SetMatrix("_Rotation", ViewerRotationMatrix());
        _cylindricalMaterial.SetFloat("_HFOV", cylindricalHFOV);
        _cylindricalMaterial.SetFloat("_VFOV", cylindricalVFOV);
        _cylindricalMaterial.SetFloat("_Exposure", exposure);
        _cylindricalMaterial.SetFloat("_Gamma", outputGamma);
        SetOutputSize(_cylindricalMaterial);
        Graphics.Blit(null, _finalSenderRT, _cylindricalMaterial);
        return true;
    }

    bool RenderPacked(bool strip)
    {
        if (!EnsureMaterial(ref _packMaterial, sh_CubemapPack, "cubemap packing")) return false;
        _packMaterial.SetTexture("_Cubemap", _cubemapRT);
        _packMaterial.SetMatrix("_Rotation", ViewerRotationMatrix());
        _packMaterial.SetFloat("_Layout", strip ? 1f : 0f);
        _packMaterial.SetFloat("_Exposure", exposure);
        _packMaterial.SetFloat("_OutputGamma", outputGamma);
        _packMaterial.SetFloat("_DiagnosticOverlay", diagnosticOverlay ? 1f : 0f);
        Graphics.Blit(null, _finalSenderRT, _packMaterial);
        return true;
    }

    bool RenderFisheye(float fovDeg)
    {
        if (!EnsureMaterial(ref _fisheyeMaterial, sh_Fisheye, "fisheye")) return false;
        _fisheyeMaterial.SetTexture("_Cubemap", _cubemapRT);
        _fisheyeMaterial.SetMatrix("_Rotation", ViewerRotationMatrix());
        _fisheyeMaterial.SetFloat("_FovDeg", fovDeg);
        _fisheyeMaterial.SetFloat("_Exposure", exposure);
        _fisheyeMaterial.SetFloat("_OutputGamma", outputGamma);
        Graphics.Blit(null, _finalSenderRT, _fisheyeMaterial);
        return true;
    }

    bool RenderEac()
    {
        if (!EnsureMaterial(ref _eacMaterial, sh_Eac, "EAC packing")) return false;
        _eacMaterial.SetTexture("_Cubemap", _cubemapRT);
        _eacMaterial.SetMatrix("_Rotation", ViewerRotationMatrix());
        _eacMaterial.SetFloat("_Exposure", exposure);
        _eacMaterial.SetFloat("_OutputGamma", outputGamma);
        Graphics.Blit(null, _finalSenderRT, _eacMaterial);
        return true;
    }

    bool RenderSourceTruthPattern()
    {
        // Truth pattern layout indices only cover Equirect/Cylindrical/Cross/Strip.
        // Newer modes fall back to a clear warning instead of a wrong layout.
        if ((int)mode > (int)SpaceWeaveOutputMode.CubemapStrip)
        {
            _warning = "Source truth pattern is only defined for Equirect/Cylindrical/Cube Cross/Strip. Switch mode or use live content.";
            ClearFinalTexture(Color.black);
            return false;
        }
        if (!EnsureMaterial(ref _truthMaterial, sh_SourceTruth, "source truth pattern")) return false;
        _truthMaterial.SetFloat("_Layout", (float)mode);
        _truthMaterial.SetFloat("_HFOV", cylindricalHFOV);
        _truthMaterial.SetFloat("_VFOV", cylindricalVFOV);
        _truthMaterial.SetFloat("_TimeSeconds", _truthPatternFrozen ? 0f : Time.unscaledTime);
        SetOutputSize(_truthMaterial);
        Graphics.Blit(null, _finalSenderRT, _truthMaterial);
        return true;
    }

    void SetOutputSize(Material material)
    {
        material.SetVector("_OutputSize", new Vector4(
            _finalSenderRT.width,
            _finalSenderRT.height,
            1f / _finalSenderRT.width,
            1f / _finalSenderRT.height));
    }

    bool EnsureMaterial(ref Material material, Shader shader, string label)
    {
        if (material != null)
        {
            if (material.shader != null && material.shader.isSupported && material.passCount > 0)
                return true;
            DestroyObject(material);
            material = null;
        }

        if (shader == null)
            return RenderShaderFallback(label, "shader reference is null");
        if (!shader.isSupported)
            return RenderShaderFallback(label, "shader is unsupported or failed to compile");

        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        if (material.passCount <= 0)
        {
            DestroyObject(material);
            material = null;
            return RenderShaderFallback(label, "material contains no valid passes");
        }
        _lastShaderFailure = string.Empty;
        return true;
    }

    bool RenderShaderFallback(string label, string reason)
    {
        string size = _finalSenderRT != null
            ? _finalSenderRT.width + "x" + _finalSenderRT.height
            : "no final RT";
        _warning = $"FALLBACK - {label}: {reason} | {mode} | {size}";
        _lastFallbackRendered = true;
        if (_lastShaderFailure != _warning)
        {
            Debug.LogError("[SpaceWeave] " + _warning, this);
            _lastShaderFailure = _warning;
        }
        if (_finalSenderRT == null) return false;

        string key = mode + "|" + _finalSenderRT.width + "x" + _finalSenderRT.height;
        if (_fallbackTexture == null || _fallbackKey != key)
        {
            DestroyObject(_fallbackTexture);
            _fallbackTexture = SpaceWeaveFallbackPattern.Build(
                mode, _finalSenderRT.width, _finalSenderRT.height);
            _fallbackKey = key;
        }
        Graphics.Blit(_fallbackTexture, _finalSenderRT);
        return false;
    }

    void ClearFinalTexture(Color colour)
    {
        if (_finalSenderRT == null || !_finalSenderRT.IsCreated()) return;
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = _finalSenderRT;
        GL.Clear(true, true, colour);
        RenderTexture.active = previous;
    }

    void ReleaseRuntimeResources()
    {
        if (spoutSender != null && spoutSender.sourceTexture == _finalSenderRT)
            spoutSender.sourceTexture = null;
        if (ndiSender != null && ndiSender.sourceTexture == _finalSenderRT)
            ndiSender.sourceTexture = null;
        DestroyRenderTexture(ref _finalSenderRT);
        DestroyRenderTexture(ref _cubemapRT);
        DestroyObject(_equirectMaterial);
        DestroyObject(_cylindricalMaterial);
        DestroyObject(_packMaterial);
        DestroyObject(_fisheyeMaterial);
        DestroyObject(_eacMaterial);
        DestroyObject(_truthMaterial);
        DestroyObject(_fallbackTexture);
        _equirectMaterial = _cylindricalMaterial = _packMaterial = _fisheyeMaterial =
            _eacMaterial = _truthMaterial = null;
        _fallbackTexture = null;
        _fallbackKey = string.Empty;
        _builtFaceSize = 0;
    }

    static void DestroyRenderTexture(ref RenderTexture texture)
    {
        if (texture == null) return;
        texture.Release();
        DestroyObject(texture);
        texture = null;
    }

    static void DestroyObject(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }

    protected virtual void OnGUI()
    {
        if (!showRuntimeStatus && !diagnosticOverlay && !testPatternMode) return;
        string rt = _finalSenderRT == null
            ? "missing"
            : $"{_finalSenderRT.name} {_finalSenderRT.width}x{_finalSenderRT.height} " +
              $"{_finalSenderRT.graphicsFormat} sRGB={_finalSenderRT.sRGB}";
        string source = spoutSender == null || spoutSender.sourceTexture == null
            ? "none"
            : spoutSender.sourceTexture.name;
        string state = string.IsNullOrEmpty(_warning) ? "CONTRACT OK" : _warning;
        GUI.Box(new Rect(10, 10, 820, 150),
            "SpaceWeave Final Sender Contract\n" +
            $"{mode} | {rt}\n" +
            $"Spout {SenderName} | Texture={source} | exact={SpoutSourceMatchesFinal}\n" +
            $"rendered={_lastFrameFullyRendered} fallback={_lastFallbackRendered} " +
            $"unexpectedMagenta={_lastUnexpectedMagenta} frozen={_truthPatternFrozen}\n" +
            state);
    }
}
}
