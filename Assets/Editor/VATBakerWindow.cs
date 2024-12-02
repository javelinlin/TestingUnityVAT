// jave.lin : 2024/11/30
// 参考 : https://github.com/fuqunaga/VatBaker            (这个没有问题)
// 参考 : https://github.com/sandwichpuissant/Unity-VAT   (这个有问题)
// 其他 : https://github.com/Unity-Technologies/Animation-Instancing (这个是 unity技术官方的 github 仓库，里面的一些 实现思路 和 API 使用可以参考，但是不建议使用，因为很多 issue 问题没解决)
// 如果要了解实现原理，可以参考这个简单实现的工具类
// 里面写了比较清晰的代码注释，遍历阅读理解

using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public class VATBakerWindow : EditorWindow
{
    // jave.lin : 顶点不能超过 4096 否则贴图可能会有不兼容问题
    // 其实后续可以使用一个 烘焙骨骼的方案，可以参考市面上的一些 插件来实现
    private const int MAX_TEXTURE_SIZE = 4096;

    private AnimationClip clip;
    private GameObject skinGameObjRoot;
    private float samplingRate = 60.0f;

    private bool hasBaked = false;
    private Texture2D bakedtexture;
    private float clipDuration;

    [MenuItem("Tools/VAT Baker Window...")]
    static void Init()
    {
        VATBakerWindow window = (VATBakerWindow)GetWindow(typeof(VATBakerWindow));
        window.Show();
    }

    private void OnGUI()
    {
        clip = (AnimationClip)EditorGUILayout.ObjectField("Animation clip", clip, typeof(AnimationClip), false);
        skinGameObjRoot =
            (GameObject)EditorGUILayout.ObjectField("Animated GameObject", skinGameObjRoot, typeof(GameObject), true);
        EditorGUILayout.Space();
        samplingRate = EditorGUILayout.FloatField("Sampling rate (times/sec)", samplingRate);

        SkinnedMeshRenderer skin = null;
        if (skinGameObjRoot != null)
        {
            skin = skinGameObjRoot.GetComponentInChildren<SkinnedMeshRenderer>();
        }

        GUI.enabled =
            clip &&
            skinGameObjRoot &&
            skin &&
            skin.sharedMesh
            && samplingRate > 0;

        if (GUILayout.Button("Generate"))
            GenerateBakedTexture();

        GUI.enabled = true;

        if (hasBaked)
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Baked Info:");
            EditorGUI.indentLevel++;
            EditorGUILayout.ObjectField("BakedTex: ", bakedtexture, typeof(Object), false);
            EditorGUILayout.FloatField("ClipDuration: ", clipDuration);
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }
    }

    public void GenerateBakedTexture()
    {
        var skinRenderer = skinGameObjRoot.GetComponentInChildren<SkinnedMeshRenderer>();
        
        // ===========================
        // jave.lin : 计算 需要烘焙的纹理尺寸 : {frameCount} x {vertexCount}
        // ===========================
        int vertexCount = skinRenderer.sharedMesh.vertexCount;
        if (vertexCount > MAX_TEXTURE_SIZE)
        {
            var msg =
                $"The Num of Model's ({skinRenderer.sharedMesh.name}) Vertex can not more than : {MAX_TEXTURE_SIZE}";
            EditorUtility.DisplayDialog("Error", msg, "OK");
            return;
        }

        // jave.lin : texture width 对应 动画时长 * 动画采样率 = 总帧数 
        int frameCount = Mathf.CeilToInt(clip.length * samplingRate);
        if (frameCount > MAX_TEXTURE_SIZE)
        {
            var msg =
                $"The Num of BakedFrameCount : (clip_dur({clip.length}) * sampleRate({samplingRate} = {frameCount})) can not more than : {MAX_TEXTURE_SIZE}";
            EditorUtility.DisplayDialog("Error", msg, "OK");
            return;
        }
        
        // ===========================
        // jave.lin : 获取顶点缓存数据
        // ===========================

        // jave.lin : 保存在纹理的所有帧的顶点坐标 [frameIDX][vertexIDX]
        // 贴图的 横向对应 : 帧ID， 纵向对应 : 顶点ID
        Vector3[][] vertexPosBuffer_WriteToTex = new Vector3[frameCount][];
        Mesh bakedMesh = new Mesh();

        // Undo.RegisterFullObjectHierarchyUndo(animatedGameObject, "Sample animation");

        // jave.lin : 开始烘焙
        // jave.lin : 将对应 skinned mesh renderer 下的坐标系转换到 root 坐标系下
        var toRootMatrix = skinGameObjRoot.transform.worldToLocalMatrix * skinRenderer.transform.localToWorldMatrix;
        // jave.lin : 根据采样率计算出每次迭代的 delta time
        var deltaTime = 1.0f / samplingRate;
        // jave.lin : 按照帧率来烘焙
        var vertexHelper = new List<Vector3>();
        for (int frameIDX = 0; frameIDX < frameCount; frameIDX++)
        {
            // jave.lin : 修改 skinned mesh renderer 中的内存网格
            clip.SampleAnimation(skinGameObjRoot, deltaTime * frameIDX);
            // jave.lin : 将修改后的 skinned mesh renderer 网格模型坐标另一个 mesh 中
            skinRenderer.BakeMesh(bakedMesh);
            // jave.lin : 根据烘焙的结果提起 顶点坐标
            bakedMesh.GetVertices(vertexHelper);
            // jave.iln : 将每个顶点坐标转换到 root 坐标系下
            for (int vertexIDX = 0; vertexIDX < vertexHelper.Count; vertexIDX++)
            {
                vertexHelper[vertexIDX] = toRootMatrix.MultiplyPoint(vertexHelper[vertexIDX]);
            }

            // jave.lin : 塞到 备写入 的
            vertexPosBuffer_WriteToTex[frameIDX] = vertexHelper.ToArray();
        }

        // Undo.PerformUndo();

        // ===========================
        // jave.lin : 开始填充烘焙的纹理数据
        // ===========================
        // jave.lin : 没有 RGBHalf，只有 RGBAHalf，白白浪费了 A 通道的数据
        Texture2D texture = new Texture2D(
            frameCount,
            vertexCount,
            TextureFormat.RGBAHalf,
            false, true);
        texture.filterMode = FilterMode.Bilinear;
        texture.anisoLevel = 0;
        for (int frameIDX = 0; frameIDX < frameCount; frameIDX++)
        {
            for (int vertexIDX = 0; vertexIDX < vertexPosBuffer_WriteToTex[frameIDX].Length; vertexIDX++)
            {
                var pos = vertexPosBuffer_WriteToTex[frameIDX][vertexIDX];
                var saveVal = new Color(pos.x, pos.y, pos.z);
                texture.SetPixel(frameIDX, vertexIDX, saveVal);
            }
        }
        texture.Apply();

        // ===========================
        // jave.lin : 保存烘焙纹理
        // ===========================
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Texture",
            $"{skinRenderer.sharedMesh.name}_VATTexture_{clip.name}",
            "asset",
            "Select destination");

        if (string.IsNullOrEmpty(path))
        {
            EditorUtility.DisplayDialog("Error", "Path is invalid!", "OK");
            DestroyExt(texture);
            return;
        }
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(texture, path);

        // ===========================
        // jave.lin : 创建 VATBaked_skinName 的GO显示在旁边
        // ===========================
        var bakedGOName = $"VATBaked_{skinGameObjRoot.name}";
        var bakedGO = GameObject.Find(bakedGOName);
        bakedGO = bakedGO != null ? bakedGO : new GameObject(bakedGOName);

        bakedGO.transform.position = skinGameObjRoot.transform.position + new Vector3(1.25f, 0f, 0f);
        bakedGO.transform.localScale = skinGameObjRoot.transform.localScale;
        bakedGO.transform.rotation = skinGameObjRoot.transform.rotation;

        var mf = bakedGO.GetComponent<MeshFilter>();
        mf = mf == null ? bakedGO.AddComponent<MeshFilter>() : mf;

        var mr = bakedGO.GetOrAddComponent<MeshRenderer>();
        mr = mr == null ? bakedGO.AddComponent<MeshRenderer>() : mr;

        mf.sharedMesh = skinRenderer.sharedMesh;
        var src_mat = skinRenderer.sharedMaterial;
        var bakedGOMat = mr.sharedMaterial;
        if (bakedGOMat == null)
        {
            var matPath = "Assets/TestingArts/Test_TestingUnityVAT_V4.mat";
            bakedGOMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (bakedGOMat == null)
            {
                bakedGOMat = new Material(Shader.Find("Test/TestingUnityVAT_V4"));
                AssetDatabase.CreateAsset(bakedGOMat, matPath);
            }
        }
        else if (bakedGOMat.shader != src_mat.shader)
        {
            bakedGOMat.shader = Shader.Find("Test/TestingUnityVAT_V4");
        }

        mr.sharedMaterial = bakedGOMat;
        bakedGOMat.SetTexture("_MainTex", src_mat.GetTexture("_MainTex"));
        bakedGOMat.SetTexture("_VATTex", texture);
        bakedGOMat.SetFloat("_Duration", clip.length);
        bakedGOMat.SetFloat("_FPS", samplingRate);
        
        // ===========================
        // jave.lin : 将原来的 skinRendererGO 里面的 animator 设置默认的 animator，方便播放直接查看
        // ===========================
        var animator = skinGameObjRoot.GetComponent<Animator>();
        if (animator != null)
        {
            AnimatorController animatorController = animator.runtimeAnimatorController as AnimatorController;
            if (animatorController != null)
            {
                // 设定要修改的层索引，0表示默认层
                int layerIndex = 0;
                var layer = animatorController.layers[layerIndex];

                // 设置默认状态
                var defualtState = layer.stateMachine.states.FirstOrDefault(s => s.state.name == clip.name).state;
                layer.stateMachine.defaultState = defualtState;
            }
        }

        // ===========================
        // jave.lin : 更新已烘焙数据
        // ===========================
        hasBaked = true;
        clipDuration = clip.length;
        bakedtexture = (Texture2D)AssetDatabase.LoadMainAssetAtPath(path);
    }

    private static void DestroyExt(UnityEngine.Object obj)
    {
        if (Application.isPlaying)
            GameObject.Destroy(obj);
        else
            GameObject.DestroyImmediate(obj, true);
    }
}