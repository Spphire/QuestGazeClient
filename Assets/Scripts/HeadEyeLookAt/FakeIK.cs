using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
//using Unity.VisualScripting;
using UnityEngine;

//=https://zhuanlan.zhihu.com/p/73449702

[System.Serializable]
public class FakeIKBone
{
    public FakeIKBone(Transform setBone, Vector3 setAxis)
    {
        Bone = setBone;
        axis = setAxis;
    }

    public Transform Bone;

    private Transform boneParent;
    public Transform BoneParent
    {
        get
        {
            if (boneParent == null)
            {
                boneParent = Bone.parent;
            }
            return boneParent;
        }
    }
    // 骨骼转动速度
    public float Speed = 30;
    // 骨骼恢复速度
    public float RecoverSpeed = 20;
    // 世界坐标系的绿色轴
    public Vector3 axis = Vector3.up;
    // 世界坐标系的绿色轴方向
    protected Vector3 transformAxis { get { return Bone.rotation * axis; } }
    // 本地坐标系的绿色轴方向
    protected Vector3 localTransformAxis { get { return Bone.localRotation * axis; } }
    // 当前转动四元数
    protected Quaternion currentQuaternion = Quaternion.identity;
    // 旋转函数
    public virtual void RotateToTarget(Vector3 targetPosition) { }
}

[System.Serializable]
public class FakeHeadIKBone : FakeIKBone
{
    // 头与观察点超过AngleMove之后，头开始转动
    public float AngleMove = 4;
    // 头一直可以转到AngleStay，之后保持直到超过AngleRecover
    public float MaxHeadRiseAngle = 10;
    public float MaxHeadDownAngle = 20;
    public float AngleStay = 50;//20
    public float AngleRecover = 110;//90

    [HideInInspector]
    public bool InRecover;

    public FakeHeadIKBone(Transform setBone, Vector3 setAxis) : base(setBone, setAxis)
    {
        
    }

    public override void RotateToTarget(Vector3 targetPosition)
    {
        if (Bone == null) return;
        //旋转目标向量
        Vector3 baseTargetDir = targetPosition - Bone.position;
        //当前骨骼绿色轴 与 目标向量的夹角
        float angle = Vector3.Angle(transformAxis, baseTargetDir);
        InRecover = false;
        //判断一下夹角
        if (angle <= AngleMove)
        {
            //啥也不干，如果发现不是正常角度，恢复到正常角度
            currentQuaternion = Quaternion.RotateTowards(currentQuaternion, Quaternion.identity, RecoverSpeed * Time.deltaTime);
        }
        else if (angle > AngleMove && angle <= AngleRecover)
        {
            // 旋转轴
            Vector3 axisTemp = Vector3.Cross(transformAxis, baseTargetDir).normalized;
            float angleTemp = angle - AngleMove;
            if (angleTemp > AngleStay - AngleMove)
            {
                //超出AngleStay，维持头的这个旋转
                angleTemp = AngleStay - AngleMove;
            }

            //真正的旋转
            Quaternion quaternion = Quaternion.AngleAxis(angleTemp, axisTemp);

            //这里后面说，主要是做一个保持头部水平的矫正
            Vector3 realBlue = quaternion * Bone.forward;
            float realAngle = 90 - Vector3.Angle(realBlue, Vector3.up);

            //矫正Bone 的 蓝色轴，使之水平
            //Quaternion quaternionTemp = Quaternion.AngleAxis(CorrectAngle * axisTemp.y, Bone.up);
            Quaternion quaternionCorrect = Quaternion.AngleAxis(realAngle, Bone.up);

            //quaternionCorrect * quaternion == 先转quaternion, 再转矫正quaternionCorrect 
            quaternion = quaternionCorrect * quaternion;

            currentQuaternion = Quaternion.RotateTowards(currentQuaternion, quaternion, Speed * Time.deltaTime);
        }
        else if (angle > AngleRecover)
        {
            //恢复 到正常角度
            InRecover = true;
            currentQuaternion = Quaternion.RotateTowards(currentQuaternion, Quaternion.identity, RecoverSpeed * Time.deltaTime);
        }
        //Debug.Log(currentQuaternion.eulerAngles.x);

        float clampXAngle = Mathf.Clamp(currentQuaternion.eulerAngles.x > 180 ? currentQuaternion.eulerAngles.x - 360 : currentQuaternion.eulerAngles.x, -MaxHeadRiseAngle, MaxHeadDownAngle);
        currentQuaternion = Quaternion.Euler(clampXAngle, currentQuaternion.eulerAngles.y, currentQuaternion.eulerAngles.z);
        Bone.rotation = currentQuaternion * Bone.rotation;
    }
}

[System.Serializable]
public class FakeEyeIKBone : FakeIKBone
{
    public bool isLeft;
    public ModelType modelType;
    //眼睛水平最大转动角度
    public float AngleXout = 12;
    public float AngleXin = 4;
    //眼睛垂直最大转动角度
    public float AngleY = 4;

    // 依赖于头部是否恢复
    [HideInInspector]
    public bool NeedRecoverEye;

    public FakeEyeIKBone(Transform setBone, Vector3 setAxis, bool isLeft, ModelType modelType) : base(setBone, setAxis)
    {
        this.isLeft = isLeft;
        this.modelType = modelType;
    }

    public override void RotateToTarget(Vector3 targetPosition)
    {
        if (Bone == null) return;

        if (NeedRecoverEye)
        {
            currentQuaternion = Quaternion.RotateTowards(currentQuaternion, Quaternion.identity, RecoverSpeed * Time.deltaTime);
        }
        else
        {
            //世界坐标系的目标向量
            Vector3 baseTargetDir = targetPosition - Bone.position;
            //转到本地坐标系的目标向量，一定要用BoneParent转换。。。一开始没注意到这里，总是转不对，经常被模型翻白眼。。。
            var targetDir = BoneParent.InverseTransformDirection(baseTargetDir);
            //本地坐标系下的蓝色轴方向
            Vector3 forwardTemp = BoneParent.InverseTransformDirection(Bone.forward);
            //以下计算都在本地坐标系下计算
            //计算一个本地坐标系下，绿色轴到目标向量的旋转轴 rotateAxis
            Vector3 rotateAxis = Vector3.Cross(localTransformAxis, targetDir);
            //转动角度
            float targetAngle = Vector3.Angle(targetDir, localTransformAxis);
            //根据旋转轴 和 本地坐标系蓝色轴 计算一个权重，用于在AngleX和AngleY中做插值（很魔性的计算。。。）
            //这里一定要做先归一化，再点乘
            float cosAAA = Mathf.Abs(Vector3.Dot(rotateAxis.normalized, forwardTemp.normalized));
            //平滑过渡一下
            float angle = 0;
            if (isLeft)
            {
                angle = Vector3.Dot(rotateAxis,HeadEyeController.GetRotateAxis(modelType)) < 0 ? Mathf.SmoothStep(AngleXout, AngleY, cosAAA) : Mathf.SmoothStep(AngleXin, AngleY, cosAAA);
            }
            else
            {
                angle = Vector3.Dot(rotateAxis, HeadEyeController.GetRotateAxis(modelType)) > 0 ? Mathf.SmoothStep(AngleXout, AngleY, cosAAA) : Mathf.SmoothStep(AngleXin, AngleY, cosAAA);
            }

            if (targetAngle > angle)
            {
                targetAngle = angle;
            }

            Quaternion targetQuaternion = Quaternion.AngleAxis(targetAngle, rotateAxis);
            currentQuaternion = Quaternion.RotateTowards(currentQuaternion, targetQuaternion, Speed * Time.deltaTime);
        }

        Bone.localRotation = currentQuaternion * Bone.localRotation;
    }
}

[System.Serializable]
public class FakeEyeIKBlendShape : FakeIKBone
{
    public bool isLeft;
    public ModelType modelType;
    public SkinnedMeshRenderer faceEyeSkin;
    //眼睛水平最大转动角度
    public float AngleXout = 12;
    public float AngleXin = 4;
    //眼睛垂直最大转动角度
    public float AngleY = 4;

    // 依赖于头部是否恢复
    [HideInInspector]
    public bool NeedRecoverEye;

    public float yaw;
    public float pitch;
    public float row;
    public FakeEyeIKBlendShape(Transform setBone, Vector3 setAxis, bool isLeft, ModelType modelType, SkinnedMeshRenderer faceEyeSkin) : base(setBone, setAxis)
    {
        this.isLeft = isLeft;
        this.modelType = modelType;
        this.faceEyeSkin = faceEyeSkin;
    }
    
    public override void RotateToTarget(Vector3 targetPosition)
    {
        if (Bone == null) return;

        if (NeedRecoverEye)
        {
            currentQuaternion = Quaternion.RotateTowards(currentQuaternion, Quaternion.identity, RecoverSpeed * Time.deltaTime);
        }
        else
        {
            //世界坐标系的目标向量
            Vector3 baseTargetDir = targetPosition - Bone.position;
            //转到本地坐标系的目标向量，一定要用BoneParent转换。。。一开始没注意到这里，总是转不对，经常被模型翻白眼。。。
            var targetDir = Bone.InverseTransformDirection(baseTargetDir);
            
            Quaternion targetQuaternion = Quaternion.FromToRotation(Vector3.up, targetDir);

            currentQuaternion = Quaternion.RotateTowards(currentQuaternion, targetQuaternion, Speed * Time.deltaTime);
        }

        //Bone.localRotation = currentQuaternion * Bone.localRotation;
        Vector3 euler = currentQuaternion.eulerAngles;
        yaw = euler.x > 180f ? euler.x-360f:euler.x;
        pitch = euler.y > 180f ? euler.y-360f:euler.y;
        row = euler.z > 180f ? euler.z-360f:euler.z;
        float RL = -yaw / 50 * 100;
        float UD = row / 30 * 100;
        if (UD>=0)
        {
            faceEyeSkin.SetBlendShapeWeight(0,math.clamp(UD,0,100));
            faceEyeSkin.SetBlendShapeWeight(1,0);
        }
        else
        {
            faceEyeSkin.SetBlendShapeWeight(0,0);
            faceEyeSkin.SetBlendShapeWeight(1,math.clamp(-UD,0,100));
        }

        if (RL >= 0)
        {
            faceEyeSkin.SetBlendShapeWeight(2,math.clamp(RL,0,100));
            faceEyeSkin.SetBlendShapeWeight(3,0);
        }
        else
        {
            faceEyeSkin.SetBlendShapeWeight(2,0);
            faceEyeSkin.SetBlendShapeWeight(3,math.clamp(-RL,0,100));
        }
    }
}