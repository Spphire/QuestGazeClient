using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class RecorderPanelMaterialAssetSanitizer
{
    private static readonly string[] RendererMaterialPaths =
    {
        "Assets/EyeTracking/Materials/RecorderPhysicalPanel_Background.mat",
        "Assets/EyeTracking/Materials/RecorderPhysicalPanel_BackgroundMesh.mat",
        "Assets/EyeTracking/Materials/RecorderPhysicalPanel_RecorderHandler.mat",
        "Assets/EyeTracking/Materials/RecorderPhysicalPanel_RecorderHandlerMesh.mat",
        "Assets/EyeTracking/Materials/RecorderPhysicalPanel_StartButton.mat",
        "Assets/EyeTracking/Materials/RecorderPhysicalPanel_StartButtonMesh.mat",
        "Assets/EyeTracking/Materials/RecorderPhysicalPanel_StopButton.mat",
        "Assets/EyeTracking/Materials/RecorderPhysicalPanel_StopButtonMesh.mat",
        "Assets/EyeTracking/Materials/RecorderPanelStable.mat"
    };

    private static readonly string[] GraphicMaterialPaths =
    {
        "Assets/EyeTracking/Materials/RecorderPanelUIDefault.mat"
    };

    [MenuItem("EyeTracking/Diagnostics/Sanitize Recorder Panel Material Assets")]
    public static void SanitizeRecorderPanelMaterialAssets()
    {
        Shader rendererShader = Shader.Find("Universal Render Pipeline/Unlit") ??
                                AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Unlit.shader") ??
                                Shader.Find("Universal Render Pipeline/Simple Lit") ??
                                Shader.Find("Universal Render Pipeline/Lit") ??
                                Shader.Find("Sprites/Default") ??
                                Shader.Find("UI/Default") ??
                                Shader.Find("Unlit/Color") ??
                                Shader.Find("Standard") ??
                                Shader.Find("Oculus/Unlit") ??
                                Shader.Find("Oculus/Unlit Transparent Color") ??
                                AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader");
        Shader graphicShader = Shader.Find("UI/Default") ??
                               Shader.Find("Sprites/Default") ??
                               rendererShader;

        if (rendererShader == null)
        {
            throw new InvalidOperationException("No safe renderer shader was found for recorder panel material assets.");
        }

        for (int i = 0; i < RendererMaterialPaths.Length; i++)
        {
            SanitizeMaterial(RendererMaterialPaths[i], rendererShader, opaque: true);
        }

        for (int i = 0; i < GraphicMaterialPaths.Length; i++)
        {
            SanitizeMaterial(GraphicMaterialPaths[i], graphicShader, opaque: false);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RecorderPanelMaterialAssetSanitizer] Recorder panel material assets now use build-included safe shaders.");
    }

    private static void SanitizeMaterial(string assetPath, Shader shader, bool opaque)
    {
        if (!File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath)))
        {
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (material == null)
        {
            return;
        }

        material.shader = shader;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white);
        }

        if (material.HasProperty("_Color") && material.GetColor("_Color") == default)
        {
            material.SetColor("_Color", Color.white);
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", Texture2D.whiteTexture);
        }

        if (opaque)
        {
            material.renderQueue = -1;
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 0f);
            }
        }

        EditorUtility.SetDirty(material);
    }
}
