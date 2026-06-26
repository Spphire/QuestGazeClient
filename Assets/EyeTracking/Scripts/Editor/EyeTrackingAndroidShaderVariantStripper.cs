using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class EyeTrackingAndroidShaderVariantStripper : IPreprocessShaders
{
    private const int MaxForwardLitVariants = 128;
    private const int MaxOtherLitPassVariants = 32;

    public int callbackOrder => 10000;

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        if (shader == null ||
            data == null ||
            data.Count == 0 ||
            EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            return;
        }

        if (shader.name != "Universal Render Pipeline/Lit")
        {
            return;
        }

        int keepCount = snippet.passName == "ForwardLit"
            ? MaxForwardLitVariants
            : MaxOtherLitPassVariants;
        if (data.Count <= keepCount)
        {
            return;
        }

        int originalCount = data.Count;
        for (int i = data.Count - 1; i >= keepCount; i--)
        {
            data.RemoveAt(i);
        }

        // Keep Android builds quiet: Unity logs stack traces for each build-time Debug.Log,
        // which can dominate build time when shader stripping runs across many passes.
    }
}
