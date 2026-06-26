using Anaglyph.XRTemplate.DepthKit;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public class EnvironmentMapper : MonoBehaviour
	{
		public static EnvironmentMapper Instance { get; private set; }

		[SerializeField] private ComputeShader shader = null;
		[SerializeField] private float metersPerVoxel = 0.05f;
		[SerializeField] private float dispatchesPerSecond = 30f;

		[SerializeField] private RenderTexture volume;

        public Bounds WorldBounds
        {
            get
            {
                Vector3 size = new Vector3(
                    volume.width * metersPerVoxel,
                    volume.height * metersPerVoxel,
                    volume.volumeDepth * metersPerVoxel
                );

                return new Bounds(Vector3.zero, size);
            }
        }
        
		private int vWidth => volume.width;
		private int vHeight => volume.height;
		private int vDepth => volume.volumeDepth;
		
		[SerializeField] private float maxEyeDist = 7f;
		public float MaxEyeDist => maxEyeDist;

		[SerializeField] private float minEyeDist = 1f;
		public float MinEyeDist => minEyeDist;

		private ComputeKernel clearKernel;
		private ComputeKernel integrateKernel;
		private ComputeKernel raycastKernel;
        
        private ComputeKernel raycastBatchKernel;

        private ComputeBuffer batchOrigins;
        private ComputeBuffer batchDirs;
        private ComputeBuffer batchResults;


		private int viewID => DepthKitDriver.agDepthView_ID;
		private int projID => DepthKitDriver.agDepthProj_ID;

		private int viewInvID => DepthKitDriver.agDepthViewInv_ID;
		private int projInvID => DepthKitDriver.agDepthProjInv_ID;

		private int depthTexID => DepthKitDriver.agDepthTex_ID;
		private int normTexID => DepthKitDriver.agDepthNormTex_ID;

		private int numPlayersID = Shader.PropertyToID("numPlayers");
		private int playerHeadsWorldID = Shader.PropertyToID("playerHeadsWorld");

		// cached points within viewspace depth frustum 
		// like a 3D lookup table
		private ComputeBuffer frustumVolume;

		public List<Transform> PlayerHeads = new();
		private Vector4[] headPositions = new Vector4[512];

		private void Awake()
		{
			Instance = this;
		}

		private bool hasStarted = false;
		private void Start()
		{
			clearKernel = new(shader, "Clear");
			clearKernel.Set(nameof(volume), volume);

			integrateKernel = new(shader, "Integrate");
			integrateKernel.Set(nameof(volume), volume);

			integrateKernel = new(shader, "Integrate");

			raycastKernel = new(shader, "Raycast");
			raycastKernel.Set("rcVolume", volume);
            
            raycastBatchKernel = new(shader, "RaycastBatch");
            raycastBatchKernel.Set("rcVolume", volume);

			shader.SetInts("volumeSize", vWidth, vHeight, vDepth);
			shader.SetFloat(nameof(metersPerVoxel), metersPerVoxel);

			Clear();

			ScanLoop();

			hasStarted = true;
		}

		public void Clear()
		{
			clearKernel.DispatchGroups(volume);
		}

		private void OnEnable()
		{
			if(!hasStarted)
				ScanLoop();
		}

		private async void ScanLoop()
		{
			while (enabled)
			{
				await Awaitable.WaitForSecondsAsync(1f / dispatchesPerSecond);

				var depthTex = Shader.GetGlobalTexture(depthTexID);
				if (depthTex == null) continue;

				var normTex = Shader.GetGlobalTexture(normTexID);
				
				if (frustumVolume == null)
					Setup();

				Matrix4x4 view = Shader.GetGlobalMatrixArray(viewID)[0];
				Matrix4x4 proj = Shader.GetGlobalMatrixArray(projID)[0];

				ApplyScan(depthTex, normTex, view, proj);
			}
		}

		public void ApplyScan(Texture depthTex, Texture normTex, Matrix4x4 view, Matrix4x4 proj)//, bool useDepthFrame)
		{
			shader.SetMatrixArray(viewID, new[]{ view, Matrix4x4.zero });
			shader.SetMatrixArray(projID, new[]{ proj, Matrix4x4.zero });

			shader.SetMatrixArray(viewInvID, new[] { view.inverse, Matrix4x4.zero });
			shader.SetMatrixArray(projInvID, new[] { proj.inverse, Matrix4x4.zero });

			for(int i = 0; i < PlayerHeads.Count; i++)
			{
				Vector3 playerHead = PlayerHeads[i].position;
				headPositions[i] = playerHead;
			}

			shader.SetInt(numPlayersID, PlayerHeads.Count);
			shader.SetVectorArray(playerHeadsWorldID, headPositions);

			integrateKernel.Set(depthTexID, depthTex);
			integrateKernel.Set(normTexID, normTex);

			integrateKernel.DispatchGroups(frustumVolume.count, 1, 1);
		}

		private void Setup()
		{
			var depthProj = Shader.GetGlobalMatrixArray(DepthKitDriver.agDepthProj_ID)[0];
			FrustumPlanes frustum = depthProj.decomposeProjection;
			//frustum.zNear = 0.2f;
			frustum.zFar = maxEyeDist;

			List<Vector3> positions = new(200000);

			var f = frustum;
			// slopes 
			float ls = f.left / f.zNear;
			float rs = f.right / f.zNear;
			float ts = f.top / f.zNear;
			float bs = f.bottom / f.zNear;

			for (float z = f.zNear; z < f.zFar; z += metersPerVoxel)
			{
				float xMin = ls * z + metersPerVoxel;
				float xMax = rs * z - metersPerVoxel;

				float yMin = bs * z + metersPerVoxel;
				float yMax = ts * z - metersPerVoxel;

				for (float x = xMin; x < xMax; x += metersPerVoxel)
				{
					for (float y = yMin; y < yMax; y += metersPerVoxel)
					{
						Vector3 v = new Vector3(x, y, -z);

						if (v.magnitude > minEyeDist && v.magnitude < maxEyeDist)
							positions.Add(v);
					}
				}
			}

			frustumVolume = new ComputeBuffer(positions.Count, sizeof(float) * 3);
			// lastIntegration = new ComputeBuffer(positions.Count, sizeof())

			frustumVolume.SetData(positions);
			integrateKernel.Set(nameof(frustumVolume), frustumVolume);
		}

		private void OnDestroy()
		{
			frustumVolume?.Release();
            
            batchOrigins?.Release();
            batchDirs?.Release();
            batchResults?.Release();
		}

		private const float RaycastScaleFactor = 1000f;

		public struct RayResult
		{
			public Vector3 point;
			public float distance;
			public bool didHit;

			public RayResult(Vector3 hitPoint, float distance)
			{
				this.point = hitPoint;
				this.distance = distance;
				this.didHit = false;
			}
		}
        
        public enum RaycastMode
        {
            Surface = 0,
            Positive = 1,
            Negative = 2
        }

		public static bool Raycast(Ray ray, float maxDist, out RayResult result, RaycastMode mode, bool fallback = false)
			=> Instance.RaycastInternal(ray, maxDist, out result, mode, fallback);

		private bool RaycastInternal(Ray ray, float maxDist, out RayResult result,RaycastMode mode, bool fallback)
		{
			result = new(ray.origin, 0);
			if (maxDist == 0)
				return false;

			if (!DepthKitDriver.DepthAvailable && fallback)
			{
				// floor cast if depth isn't available

				var orig = ray.origin;
				var dir = ray.direction;
				Vector2 slope = new Vector2(dir.x, dir.z) / dir.y;

				result.point = new Vector3(slope.x * -orig.y + orig.x, 0, slope.y * -orig.y + orig.z);
				result.distance = Vector3.Distance(orig, result.point);

				return true;
			}

			shader.SetVector("rcOrig", ray.origin);
			shader.SetVector("rcDir", ray.direction);
			shader.SetFloat("rcIntScale", RaycastScaleFactor);
            
            shader.SetInt("rcMode", (int)mode);

			uint lengthInt = (uint)(maxDist * RaycastScaleFactor);

			ComputeBuffer resultBuffer = new ComputeBuffer(1, sizeof(uint));
			resultBuffer.SetData(new uint[] { lengthInt });
			raycastKernel.Set("rcResult", resultBuffer);

			int totalNumSteps = Mathf.RoundToInt(maxDist / metersPerVoxel);

			if (totalNumSteps == 0)
				return false;

			raycastKernel.DispatchGroups(totalNumSteps, 1, 1);

			//var request = await AsyncGPUReadback.Request(resultBuffer);
			//if (request.hasError)
			//	return hit;
			//var result = request.GetData<uint>();
			uint[] d = new uint[1];
			resultBuffer.GetData(d);
			uint hitDistInt = d[0];
			//result.Dispose();
			resultBuffer.Release();

			if (hitDistInt >= lengthInt)
				return false;

			float hitDist = hitDistInt / RaycastScaleFactor;

			if (hitDist >= maxDist)
				return false;

			result = new(ray.GetPoint(hitDist), hitDist);
			result.didHit = true;
			return true;
		}
        
        public static RayResult[] RaycastBatch(
            Ray[] rays,
            float maxDist,
            RaycastMode mode)
        {
            return Instance.RaycastBatchInternal(rays, maxDist, mode);
        }

        private RayResult[] RaycastBatchInternal(
            Ray[] rays,
            float maxDist,
            RaycastMode mode)
        {
            int rayCount = rays.Length;
            RayResult[] results = new RayResult[rayCount];

            if (rayCount == 0 || maxDist <= 0)
                return results;

            // ---- prepare cpu data ----
            Vector3[] origins = new Vector3[rayCount];
            Vector3[] dirs = new Vector3[rayCount];

            for (int i = 0; i < rayCount; i++)
            {
                origins[i] = rays[i].origin;
                dirs[i] = rays[i].direction.normalized;
                results[i] = new RayResult(rays[i].origin, 0);
            }

            uint maxDistInt = (uint)(maxDist * RaycastScaleFactor);

            // ---- allocate buffers (resize if needed) ----
            AllocateOrResize(ref batchOrigins, rayCount, sizeof(float) * 3);
            AllocateOrResize(ref batchDirs, rayCount, sizeof(float) * 3);
            AllocateOrResize(ref batchResults, rayCount, sizeof(uint));

            batchOrigins.SetData(origins);
            batchDirs.SetData(dirs);

            // init results buffer with max distance
            uint[] init = new uint[rayCount];
            for (int i = 0; i < rayCount; i++)
                init[i] = maxDistInt;

            batchResults.SetData(init);

            // ---- shader params ----
            shader.SetFloat("rcIntScale", RaycastScaleFactor);
            shader.SetInt("rcMode", (int)mode);
            shader.SetInt("rcRayCount", rayCount);

            int maxSteps = Mathf.CeilToInt(maxDist / metersPerVoxel);
            shader.SetInt("rcMaxSteps", maxSteps);

            raycastBatchKernel.Set("rcOrigins", batchOrigins);
            raycastBatchKernel.Set("rcDirs", batchDirs);
            raycastBatchKernel.Set("rcResults", batchResults);

            // ---- dispatch ----
            int threadX = 16; // must match compute shader
            int threadY = 4;

            int groupsX = Mathf.CeilToInt(maxSteps / (float)threadX);
            int groupsY = Mathf.CeilToInt(rayCount / (float)threadY);

            raycastBatchKernel.Dispatch(groupsX, groupsY, 1);

            // ---- readback ----
            uint[] gpuResults = new uint[rayCount];
            batchResults.GetData(gpuResults);

            // ---- build results ----
            for (int i = 0; i < rayCount; i++)
            {
                uint hitInt = gpuResults[i];
                if (hitInt >= maxDistInt)
                    continue;

                float hitDist = hitInt / RaycastScaleFactor;
                if (hitDist > maxDist)
                    continue;

                results[i] = new RayResult(
                    rays[i].GetPoint(hitDist),
                    hitDist
                );
                results[i].didHit = true;
            }

            return results;
        }
        
        private static void AllocateOrResize(
            ref ComputeBuffer buffer,
            int count,
            int stride)
        {
            if (buffer == null || buffer.count < count)
            {
                buffer?.Release();
                buffer = new ComputeBuffer(count, stride);
            }
        }
    }
}
