using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public class BakeAnimationTexture : EditorWindow
{
    private ComputeShader _bakeAnimationComputeShader;
    private string _folderPath;
    private GameObject _gameObj;
    private AnimationClip _animationClips;
    private string _animationClipsName;
    private int _bakeSteps = 1;

    //* GUI
    private readonly Color _defaultTextColor = new(0.75f, 0.75f, 0.75f, 1);
    [SerializeField] private List<List<Mesh>> _tempListMesh = new();
    private bool _checkSavePath;
    private Vector2 _scrollView;
    private bool _isManuallySetTempGameObj;
    private int _tempGameObjMode;

    //* Temp
    private GameObject _tempGameObj;
    private TimelineAsset _tempTimelineAsset;
    private PlayableDirector _tempPlayableDirector;
    private AnimationTrack _tempAnimationTrack;

    [MenuItem("Tools/UberParticle Editor/Bake AnimationTexture")]
    public static void ShowWindow()
    {
        GetWindow(typeof(BakeAnimationTexture));
    }

    private void OnGUI()
    {
        _scrollView = GUILayout.BeginScrollView(_scrollView);
        DrawInspectorGUI();
        GUILayout.EndScrollView();
    }

    private void OnEnable()
    {
        GetComputeShader();
    }

    private void GetComputeShader()
    {
        _bakeAnimationComputeShader = Resources.Load<ComputeShader>("BakeAnimationComputerShader");
    }

    private void CheckSetTempGameObj()
    {
        _tempGameObjMode = GUILayout.Toolbar(_tempGameObjMode, new[] { "Manually", "Auto" });
        switch (_tempGameObjMode)
        {
            case 0: // Manually
                _isManuallySetTempGameObj = true;
                break;
            case 1: // Auto
                _isManuallySetTempGameObj = false;
                break;
        }
    }

    private void DrawSetTempGameObjGUI(bool checkManually)
    {
        var tooltips_Auto = "1. Select the FBX from the assets, and also place the Animation files from the FBX.";
        var tooltips_Manually = "1. Select the GameObject from the scene which already set timeline, and also write down the 'Animation Clip Name'.";
        var tooltips = checkManually ? tooltips_Auto : tooltips_Manually;
        GUILayout.Label(tooltips, new GUIStyle { normal = { textColor = new Color(0.6f, 0.6f, 0.6f, 1) } });
        GUILayout.BeginVertical("HelpBox");
        {
            if (checkManually)
            {
                _gameObj = (GameObject)EditorGUILayout.ObjectField("FBX (from Scenes)", _gameObj, typeof(object), true);
                _animationClipsName = EditorGUILayout.DelayedTextField("Animation Clip Name", _animationClipsName);
                _tempGameObj = _gameObj;
            }
            else
            {
                _gameObj = (GameObject)EditorGUILayout.ObjectField("FBX (from Assets)", _gameObj, typeof(object), true);
                _animationClips = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", _animationClips, typeof(AnimationClip), false);
            }
        }
        GUILayout.EndVertical();
    }

    private void DrawInspectorGUI()
    {
        GUILayout.BeginVertical("Box");
        {
            GUILayout.Label("Bake Animation Tool",
                new GUIStyle
                {
                    alignment = TextAnchor.MiddleCenter, fontSize = 16, normal = { textColor = _defaultTextColor }, fontStyle = FontStyle.Bold
                });

            GUILayout.Space(5);

            GUILayout.Label(GUIContent.none,
                new GUIStyle
                {
                    normal = { background = Texture2D.grayTexture }, alignment = TextAnchor.MiddleCenter,
                    fixedWidth = EditorGUIUtility.currentViewWidth - 16f, fixedHeight = 1
                });
            GUILayout.Space(5);

            WaringHint();
            CheckSetTempGameObj();
            DrawSetTempGameObjGUI(_isManuallySetTempGameObj);

            GUILayout.Label(
                "2. Select the number of samples for the animation duration.\r\n【Note】The higher the number, the smoother the animation will be, but this will also proportionally increase the texture size.",
                new GUIStyle { normal = { textColor = new Color(0.6f, 0.6f, 0.6f, 1) }, wordWrap = true });

            GUILayout.BeginVertical("HelpBox");
            {
                DrawSavePathGUI();
                using (new EditorGUI.DisabledScope(_checkSavePath))
                {
                    GUILayout.BeginHorizontal();
                    {
                        _bakeSteps = EditorGUILayout.IntField("Bake Steps", _bakeSteps);
                        if (GUILayout.Button("Bake it")) CheckCanBake();
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();
        }
        GUILayout.EndVertical();
    }

    private void CheckCanBake()
    {
        var canBakeIt_Manually = _gameObj != null && !string.IsNullOrEmpty(_animationClipsName) && !string.IsNullOrEmpty(_folderPath);
        var canBakeIt_Auto = _gameObj != null && _animationClips != null && !string.IsNullOrEmpty(_folderPath);
        var canBakeIt = _isManuallySetTempGameObj ? canBakeIt_Manually : canBakeIt_Auto;

        var showErrorLog_Manually = "Please checkout the GameObject with Timeline setting, Animation Clip Name, Folder Path is already exist.";
        var showErrorLog_Auto = "Please checkout the FBX, Animation Clip, Folder Path is already exist.";
        var showErrorLog = _isManuallySetTempGameObj ? showErrorLog_Manually : showErrorLog_Auto;

        if (canBakeIt)
        {
            // Skip auto set timeline prefab.
            if (_isManuallySetTempGameObj)
                SetTimeLineToBake_Manual();
            else
                SetTimeLineToBake_Auto();
        }
        else
        {
            Debug.LogError(showErrorLog);
        }
    }

    private void SetTimeLineToBake_Manual()
    {
        if (_tempGameObj.TryGetComponent(out PlayableDirector playableDirector))
        {
            _tempPlayableDirector = playableDirector;
        }

        EditorApplication.delayCall = BakeFromTimeLine;
    }

    private void WaringHint()
    {
        GUILayout.BeginVertical("box");
        {
            GUILayout.Label(EditorGUIUtility.TrTextContentWithIcon("This tool is only for 'SkinMesh Renderer' now.", MessageType.Warning), new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 20,
                normal =
                {
                    textColor = _defaultTextColor
                }
            });
        }
        GUILayout.EndVertical();
    }

    private void SetTimeLineToBake_Auto()
    {
        _tempGameObj = Instantiate(_gameObj);
        if (!_tempGameObj.TryGetComponent(out Animator animator))
        {
            _tempGameObj.AddComponent<Animator>();
            Debug.Log("Add Animator");
        }

        _tempPlayableDirector = _tempGameObj.AddComponent<PlayableDirector>();

        if (_tempTimelineAsset == null)
            // Create a new timeline asset
            _tempTimelineAsset = CreateInstance<TimelineAsset>();

        // Set this timeline asset to the playable director
        _tempPlayableDirector.playableAsset = _tempTimelineAsset;

        // Add animation track
        _tempAnimationTrack = AddAnimationTrack(_tempTimelineAsset);

        // Bind the GameObject to the animation track
        BindTrackToGameObject(_tempAnimationTrack);

        EditorApplication.delayCall = BakeFromTimeLine;
    }

    private void BakeFromTimeLine()
    {
        _tempListMesh.Clear();
        var sharedMeshes = _tempGameObj.GetComponentsInChildren<SkinnedMeshRenderer>();
        for (var index = 0; index < sharedMeshes.Length; index++)
        {
            var eachSharedMesh = sharedMeshes[index];
            var currentMesh = eachSharedMesh.sharedMesh;
            var meshVertexCount = currentMesh.vertexCount;

            float timelineDuration = _isManuallySetTempGameObj ? (float)_tempPlayableDirector.duration : (float)_tempAnimationTrack.duration;
            var eachStepsTime = timelineDuration / _bakeSteps;
            _tempPlayableDirector.time = 0;
            _tempListMesh.Add(new List<Mesh>());

            for (var i = 0; i < _bakeSteps; i++)
            {
                _tempPlayableDirector.time += eachStepsTime;
                _tempPlayableDirector.Evaluate();
                _tempListMesh[index].Add(new Mesh());
                eachSharedMesh.BakeMesh(_tempListMesh[index][i]);
            }

            BakeDataInTex(currentMesh.name, meshVertexCount, _bakeSteps, index);
        }
    }

    private void BakeDataInTex(string currentMeshName, int meshVertexCount, int frames, int listIndex)
    {
        var textWidth = meshVertexCount;
        var vertexInfo = new List<VertInfo>();

        var positionRT = new RenderTexture(textWidth, frames, 0, RenderTextureFormat.ARGBHalf);
        var normalRT = new RenderTexture(textWidth, frames, 0, RenderTextureFormat.ARGBHalf);
        var animationClipsName = _isManuallySetTempGameObj ? _animationClipsName : _animationClips.name;

        positionRT.name = $"{_gameObj.name}_{currentMeshName}_{animationClipsName}_positionTex";
        normalRT.name = $"{_gameObj.name}_{currentMeshName}_{animationClipsName}_normalTex";

        RenderTexture[] groupRT = { positionRT, normalRT };

        foreach (var renderTex in groupRT)
        {
            renderTex.enableRandomWrite = true;
            renderTex.Create();
            RenderTexture.active = renderTex;
            GL.Clear(true, true, Color.clear);
        }

        foreach (var mesh in _tempListMesh[listIndex])
            vertexInfo.AddRange(Enumerable.Range(0, meshVertexCount).Select(idx => new VertInfo
            {
                Position = mesh.vertices[idx],
                Normal = mesh.normals[idx]
            }));

// Compute Shader Execute
        var buffer = new ComputeBuffer(vertexInfo.Count, Marshal.SizeOf(typeof(VertInfo)));
        buffer.SetData(vertexInfo);

        var kernel = _bakeAnimationComputeShader.FindKernel("CSMain");
        uint3 groupSize;
        _bakeAnimationComputeShader.GetKernelThreadGroupSizes(kernel, out groupSize.x, out groupSize.y, out groupSize.z);
        _bakeAnimationComputeShader.SetInt("VertCount", meshVertexCount);
        _bakeAnimationComputeShader.SetBuffer(kernel, "meshInfo", buffer);
        _bakeAnimationComputeShader.SetTexture(kernel, "OutPosition", positionRT);
        _bakeAnimationComputeShader.SetTexture(kernel, "OutNormal", normalRT);
        _bakeAnimationComputeShader.Dispatch(kernel, meshVertexCount / (int)groupSize.x + 1, frames / (int)groupSize.y + 1, (int)groupSize.z);

        buffer.Release();

        var posTex = Convert(positionRT);
        var norTex = Convert(normalRT);

        AssetDatabase.CreateAsset(posTex, _folderPath + $"/{positionRT.name}.asset");
        AssetDatabase.CreateAsset(norTex, _folderPath + $"/{normalRT.name}.asset");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Note: If dest save path of the CreatAsset() is already have, CreateAsset will overwrite the data but the information will be wrong with last asset data.
        //       So use activeObject to refresh the inspector GUI will fix the problem.
        Selection.activeObject = norTex;

// GC Clean
        CleanGC(ref positionRT, ref normalRT);
    }

    private void CleanGC(ref RenderTexture positionRT, ref RenderTexture normalRT)
    {
        // Do not delete user's prefab on scene when in 'Manually Mode'
        if (_isManuallySetTempGameObj)
        {
            DestroyImmediate(positionRT, true);
            DestroyImmediate(normalRT, true);
            foreach (var sharedMeshes in _tempListMesh)
            foreach (var eachMesh in sharedMeshes)
                DestroyImmediate(eachMesh, true);
        }
        else
        {
            DestroyImmediate(positionRT, true);
            DestroyImmediate(normalRT, true);
            DestroyImmediate(_tempGameObj, false);
            foreach (var sharedMeshes in _tempListMesh)
            foreach (var eachMesh in sharedMeshes)
                DestroyImmediate(eachMesh, true);
        }
    }

    private AnimationTrack AddAnimationTrack(TimelineAsset timelineAsset)
    {
        // Create an animation track
        var animationTrack = timelineAsset.CreateTrack<AnimationTrack>(null, "Animation Track");

        // Create a clip for the animation track
        animationTrack.CreateClip(_animationClips);

        return animationTrack;
    }

    private void BindTrackToGameObject(AnimationTrack animationTrack)
    {
        // Bind the GameObject to the animation track via the Playable Director
        _tempPlayableDirector.SetGenericBinding(animationTrack, _tempGameObj);
    }

    private void DrawSavePathGUI()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Folder Path");
        GUILayout.TextField(_folderPath);
        if (GUILayout.Button("Select Folder Path"))
        {
            _folderPath = EditorUtility.SaveFolderPanel("Save Path", "Assets", "");
            if (_folderPath.StartsWith(Application.dataPath)) _folderPath = "Assets" + _folderPath.Substring(Application.dataPath.Length);
        }

        _checkSavePath = string.IsNullOrEmpty(_folderPath);
        GUILayout.EndHorizontal();
    }

    private Texture2D Convert(RenderTexture rt)
    {
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false);
        RenderTexture.active = rt;
        texture.ReadPixels(Rect.MinMaxRect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = null;
        return texture;
    }

    private struct VertInfo
    {
        public Vector3 Position;
        public Vector3 Normal;
    }
}