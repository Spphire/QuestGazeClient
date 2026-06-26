using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class WUWAHeadDirPass : MonoBehaviour
{
    public Transform headBone;

    public Vector3 forward;

    public Vector3 right;

    public Renderer targetRenderer;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (headBone is null)
        {
            return;
        }

        forward = headBone.up;
        right = -headBone.forward;
        
        foreach (var material in targetRenderer.sharedMaterials)
        {
            material.SetVector("forward", forward);
            material.SetVector("right",  right);
        }
    }
}
