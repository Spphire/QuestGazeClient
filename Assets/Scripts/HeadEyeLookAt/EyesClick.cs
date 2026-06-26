using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

public class EyesClick : MonoBehaviour
{
    public string leftEyeCloseBSName;
    public string rightEyeCloseBSName;
    public int leftID;
    public int rightID;
    public SkinnedMeshRenderer faceEyeSkin;
    private float dt = 0.025f;
    public List<float> blendshapeValues = new List<float>(){ 0, 40, 70, 90, 100, 100, 90, 75, 55, 30, 20, 0 };

    public bool duringClicking;
    public float duringClickingTimer;
    public int index;
    public float timer;
    // Start is called before the first frame update
    void Start()
    {
        //blendshapeValues = new List<float>(){ 0, 40, 70, 90, 100, 100, 90, 75, 55, 30, 20, 0 };
        duringClicking = false;
        duringClickingTimer = 0f;
        
        leftID = faceEyeSkin.sharedMesh.GetBlendShapeIndex(leftEyeCloseBSName);
        rightID = faceEyeSkin.sharedMesh.GetBlendShapeIndex(rightEyeCloseBSName);
    }

    // Update is called once per frame
    void Update()
    {
        if (!duringClicking)
        {
            timer -= Time.deltaTime;
            if (timer <= 0)
            {
                duringClicking = true;
                duringClickingTimer = 0f;
                index = 0;
            }
        }
        else
        {
            duringClickingTimer+= Time.deltaTime;
            index = (int)(duringClickingTimer / dt);
            if (index >= blendshapeValues.Count)
            {
                duringClickingTimer = 0f;
                index = 0;
                timer = Random.Range(4f, 6f);
                duringClicking = false;
            }
        }

        
    }

    private void LateUpdate()
    {
        faceEyeSkin.SetBlendShapeWeight(leftID,blendshapeValues[index%blendshapeValues.Count]);
        faceEyeSkin.SetBlendShapeWeight(rightID,blendshapeValues[index%blendshapeValues.Count]);
    }

}

#if UNITY_EDITOR
[CustomEditor(typeof(EyesClick))]
public class InspectorEyesClick : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        EyesClick ec = (EyesClick)target;
        if (GUILayout.Button("眨眼"))
        {
            ec.timer = 0;
        }
    }
}
#endif