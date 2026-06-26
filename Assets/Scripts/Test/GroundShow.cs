using UnityEngine;

public class GroundShow : MonoBehaviour
{
    private float timer = 0;

    private float lifeTime = 2;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        timer += Time.deltaTime;

        float alpha = (lifeTime - timer) / lifeTime;
        
        GetComponentInChildren<MeshRenderer>().materials[0].SetColor("_BaseColor", new Color(1,0,0,alpha));

        if (timer > lifeTime)
        {
            //Destroy(gameObject);
        }
    }
}
