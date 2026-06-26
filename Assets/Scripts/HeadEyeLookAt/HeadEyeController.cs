using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ModelType
{
    Genshin,
    StarRail,
}
public class HeadEyeController : MonoBehaviour
{
    public Transform testTarget;
    public ModelType modelType;
    public Transform NeckBone;
    public Transform EyeLeft;
    public Transform EyeRight;
    public Transform Head;
    public SkinnedMeshRenderer faceEyeSkin;

    public FakeHeadIKBone HeadIK;
    public FakeEyeIKBone EyeLeftIK;
    public FakeEyeIKBone EyeRightIK;

    public FakeEyeIKBlendShape EyesIK;

    // Start is called before the first frame update
    void Start()
    {
        Vector3 axis = GetRotateAxis(modelType);
        if (NeckBone != null) HeadIK = new FakeHeadIKBone(NeckBone,Vector3.up);
        if (EyeLeft != null) EyeLeftIK = new FakeEyeIKBone(EyeLeft, axis, true, modelType);
        if (EyeRight != null) EyeRightIK = new FakeEyeIKBone(EyeRight, axis, false, modelType);
        if (EyesIK != null) EyesIK = new FakeEyeIKBlendShape(Head, axis, false, modelType, faceEyeSkin);
    }

    public static Vector3 GetRotateAxis(ModelType modelType)
    {
        Vector3 axis = new Vector3();
        switch (modelType)
        {
            case ModelType.Genshin:
                axis = -Vector3.right;
                break;
            case ModelType.StarRail:
                axis = Vector3.forward;
                break;
            default:
                axis = Vector3.forward;
                break;
        }
        return axis;
    }

    // Update is called once per frame
    private void LateUpdate()
    {
/*#if UNITY_EDITOR
        HeadIK.RotateToTarget(testTarget.position);
        //EyeLeftIK.RotateToTarget(testTarget.position);
        //EyeRightIK.RotateToTarget(testTarget.position);
        EyesIK.NeedRecoverEye = HeadIK.InRecover;
        EyesIK.RotateToTarget(testTarget.position);
#else*/
        HeadIK.RotateToTarget(Camera.main.transform.position);
        //EyeLeftIK.RotateToTarget(Camera.main.transform.position);
        //EyeRightIK.RotateToTarget(Camera.main.transform.position);
        EyesIK.NeedRecoverEye = HeadIK.InRecover;
        EyesIK.RotateToTarget(Camera.main.transform.position);
//#endif
    }
}
