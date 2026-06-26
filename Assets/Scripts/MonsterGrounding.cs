using System;
using Anaglyph.XRTemplate;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem; // 鏂拌緭鍏ョ郴缁?

public class MonsterGrounding : MonoBehaviour
{
    [Header("Prefab 寮曠敤")]
    public GameObject prefab;

    [Header("Input Guard")]
    [SerializeField] private bool requireRightAAndBWithTrigger = true;
    [SerializeField, Range(0f, 1f)] private float controllerTriggerThreshold = 0.72f;
    [SerializeField] private bool enableContinuousGrounding = false;

    // 鐢熸垚鍑烘潵鐨勫疄渚?
    private GameObject chara;

    // 璁板綍瀛愯妭鐐?
    private Transform center;
    private Transform top;
    private Transform buttom;
    
    private EnvironmentMapper.RayResult hit;
    private Ray ray;
    private Vector3 tempPos;
    private bool rightGroundingTriggerHeld;

    /// <summary>
    /// 鐢熸垚瑙掕壊骞惰褰曞叧閿瓙鑺傜偣
    /// </summary>
    public void SpawnChara()
    {
        if (prefab == null)
        {
            Debug.LogError("Prefab 鏈祴鍊硷紒");
            return;
        }

        if (chara == null)
        {
            // 瀹炰緥鍖?prefab
            chara = Instantiate(prefab, transform.position, transform.rotation);
            
            // 璁剧疆涓哄綋鍓嶇墿浣撳瓙鐗╀綋
            //chara.transform.SetParent(transform);

            // 閲嶇疆鐩稿浣嶇疆/鏃嬭浆/缂╂斁
            chara.transform.localPosition = Vector3.zero;
            chara.transform.localRotation = Quaternion.identity;
            chara.transform.localScale = Vector3.one;

            // 鍦ㄥ瓙鐗╀綋閲屾煡鎵?
            center = chara.transform.Find("center");
            top = chara.transform.Find("top");
            buttom = chara.transform.Find("buttom");

            if (center == null || top == null || buttom == null)
            {
                Debug.LogWarning("Missing required center/top/buttom child transforms; please check the prefab structure.");
            }
        }
    }
    
    public float fallSpeed = 5f;      // 鑷敱钀戒綋閫熷害
    public float floatUpSpeed = 3f;   // 涓婃诞閫熷害
    public float checkDistance = 10f; // 鏈€澶ф帰娴嬭窛绂?
    public float groundOffset = 0.00f; // 绂诲湴鍋忕Щ锛岄槻姝㈠崱浣?

    private Vector3 velocity;

    //public TMPro.TextMeshPro text;

    void Start()
    {
    }

    void Update()
    {
        // --- 宸︽墜 Y 閿鍔?groundOffset ---
        if (OVRInput.GetDown(OVRInput.RawButton.Y, OVRInput.Controller.LTouch))
        {
            groundOffset += 0.01f;
        }

        // --- 宸︽墜 X 閿噺灏?groundOffset ---
        if (OVRInput.GetDown(OVRInput.RawButton.X, OVRInput.Controller.LTouch))
        {
            groundOffset -= 0.01f;
        }


        // --- 妫€娴嬫壋鏈烘寜涓?---
        // Quest3 鐨勬墜鏌勭敤 InputSystem 閲?"trigger" 杈撳叆
        // 宸︽墜 RightHand.Controller, 鍙虫墜 LeftHand.Controller 鍙栧喅浜庤缃?
        if (GroundingConfirmDown())
        {
            Vector3 handPos = OVRInput.GetLocalControllerPosition(requireRightAAndBWithTrigger ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch);

            ray = new Ray(handPos, Vector3.down);
            if (EnvironmentMapper.Raycast(ray, checkDistance, out hit, EnvironmentMapper.RaycastMode.Negative))
            {
                float groundY = hit.point.y;
                SpawnChara();
                // 鏇存柊浣嶇疆锛堜繚鎸?y 涓嶅彉锛?
                tempPos = transform.position;
                tempPos.x = handPos.x;
                tempPos.y = groundY + groundOffset - buttom.localPosition.y;
                tempPos.z = handPos.z;
                chara.transform.position = tempPos;

                // 鑾峰彇澶撮儴浣嶇疆
                Vector3 headPos = Camera.main.transform.position;

                // 鍙湪 XZ 骞抽潰涓婃湞鍚戝ご閮?
                Vector3 dir = headPos - chara.transform.position;
                dir.y = 0f; // 蹇界暐楂樺害宸?
                if (dir.sqrMagnitude > 0.001f)
                {
                    chara.transform.rotation = Quaternion.LookRotation(dir);
                }

            }


        }

    }

    private bool GroundingConfirmDown()
    {
        if (!requireRightAAndBWithTrigger)
        {
            return OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
        }

        bool aHeld = OVRInput.Get(OVRInput.RawButton.A, OVRInput.Controller.RTouch) ||
                     OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);
        bool bHeld = OVRInput.Get(OVRInput.RawButton.B, OVRInput.Controller.RTouch) ||
                     OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch);
        bool triggerHeld = OVRInput.Get(OVRInput.RawButton.RIndexTrigger, OVRInput.Controller.RTouch) ||
                           OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger, OVRInput.Controller.RTouch) > controllerTriggerThreshold ||
                           OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                           OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch) > controllerTriggerThreshold;

        bool comboHeld = aHeld && bHeld && triggerHeld;
        bool comboDown = comboHeld && !rightGroundingTriggerHeld;
        rightGroundingTriggerHeld = comboHeld;

        return comboDown;
    }

    public void LateUpdate()
    {
        if (!enableContinuousGrounding)
        {
            return;
        }

        if (chara == null)
        {
            return;
        }
        tempPos = chara.transform.position;
        // --- 鍚戜笅妫€娴?---
        ray = new Ray(center.position, Vector3.down);
        if (EnvironmentMapper.Raycast(ray, checkDistance, out hit, EnvironmentMapper.RaycastMode.Negative))
        {
            float groundY = hit.point.y;
            if (buttom.transform.position.y > groundY + groundOffset + 0.03f)
            {
                // 鍦ㄧ┖涓紝鑷敱钀戒綋
                velocity += Physics.gravity * Time.deltaTime;
                tempPos += velocity * Time.deltaTime;
            }
            else
            {
                // 宸茬粡鍒拌揪鍦伴潰锛岃创鍒板湴闈?
                tempPos.y = math.lerp(tempPos.y, groundY + groundOffset - buttom.localPosition.y, 0.02f);
                velocity = Vector3.zero;
            }
            chara.transform.position = tempPos;
            return;
        }

        /*// --- 鍚戜笅娌℃娴嬪埌锛屽皾璇曞悜涓婃娴嬶紙鍙兘鍗″湪鍦颁笅锛?---
        ray = new Ray(tempPos, Vector3.up);
        if (EnvironmentMapper.Raycast(ray, checkDistance, out hit))
        {
            pos.y = hit.point.y + groundOffset;
            velocity = Vector3.zero;
            transform.position = pos;
            return;
        }

        // --- 涓婁笅閮芥病妫€娴嬪埌锛岀户缁嚜鐢辫惤浣?---
        velocity += Physics.gravity * Time.deltaTime;
        pos += velocity * Time.deltaTime;
        transform.position = pos;*/
        velocity = Vector3.zero;
    }
}
