using UnityEngine;

[System.Serializable]
public class FingerSolver
{
    [HideInInspector] public string fingerName;

    // 🔥 补回 SerialReceiver 需要的字段
    [System.NonSerialized] public int deviceId;

    [HideInInspector] public bool isThumb;

    [Header("多传感器硬件 ID绑定")]
    public int rootId;
    public int midId;
    public int tipId;

    [Header("指节骨骼")]
    public Transform rootBone;
    public Transform midBone;
    public Transform tipBone;

    // 🔥 修改点：拆分为3个独立的灵敏度滑块
    [Header("分段增量灵敏度")]
    [Range(0f, 2f)] public float rootSensitivity = 1.0f;
    [Range(0f, 2f)] public float midSensitivity = 1.0f;
    [Range(0f, 2f)] public float tipSensitivity = 1.0f;

    [Header("大拇指专属：生物基准")]
    public float thumbBaseYawOffset = -45f;

    [Header("🔧 运行自由调姿窗口")]
    public bool PoseEditMode = false;
    [Range(-180, 180)] public float AdjRootX, AdjRootY, AdjRootZ;
    [Range(-180, 180)] public float AdjMidX, AdjMidY, AdjMidZ;
    [Range(-180, 180)] public float AdjTipX, AdjTipY, AdjTipZ;

    [Header("模型关节限制 (防止硬件漂移导致反关节)")]
    [Range(-30f, 0f)] public float minPitch = -10f;
    [Range(0f, 160f)] public float maxPitch = 95f;
    [Range(0f, 100f)] public float maxYaw = 35f;

    [Header("三维力网格")]
    public ForceGridVisualizer forceGridPrefab;
    private ForceGridVisualizer[] _forceGrids = new ForceGridVisualizer[3];

    private float[] multiPitches = new float[3];
    private float[] multiYaws = new float[3];

    private float[] multiLastRawPitches = new float[3];
    private float[] multiLastRawYaws = new float[3];

    private Quaternion[] multiDriftBiases = new Quaternion[3] { Quaternion.identity, Quaternion.identity, Quaternion.identity };

    public float CurrentPitch => Mathf.Clamp(multiPitches[0], minPitch, maxPitch);
    public float CurrentYaw => Mathf.Clamp(multiYaws[0], -maxYaw, maxYaw);
    public float MaxPitch => maxPitch;

    private Quaternion initialRoot;
    private Quaternion initialMid;
    private Quaternion initialTip;

    public FingerSolver(string name, int rId, int mId, int tId, bool thumb, float minP, float maxP, float maxY)
    {
        fingerName = name;
        rootId = rId;
        deviceId = rId;
        midId = mId;
        tipId = tId;
        isThumb = thumb;
        minPitch = minP;
        maxPitch = maxP;
        maxYaw = maxY;
    }

    public void Init()
    {
        if (rootBone) initialRoot = rootBone.localRotation;
        if (midBone) initialMid = midBone.localRotation;
        if (tipBone) initialTip = tipBone.localRotation;

        _forceGrids[0] = CreateGrid(rootBone, "RootForceGrid");
        _forceGrids[1] = CreateGrid(midBone, "MidForceGrid");
        _forceGrids[2] = CreateGrid(tipBone, "TipForceGrid");

        bool originalEditMode = PoseEditMode;
        PoseEditMode = true;
        if (rootBone) rootBone.localRotation = initialRoot * Quaternion.Euler(AdjRootX, AdjRootY, AdjRootZ);
        if (midBone) midBone.localRotation = initialMid * Quaternion.Euler(AdjMidX, AdjMidY, AdjMidZ);
        if (tipBone) tipBone.localRotation = initialTip * Quaternion.Euler(AdjTipX, AdjTipY, AdjTipZ);
        PoseEditMode = originalEditMode;
    }

    private ForceGridVisualizer CreateGrid(Transform parentBone, string objName)
    {
        if (parentBone == null) return null;
        GameObject gridObj = new GameObject(objName);
        gridObj.transform.SetParent(parentBone, false);
        gridObj.transform.localPosition = Vector3.zero;
        gridObj.transform.localRotation = Quaternion.identity;
        return gridObj.AddComponent<ForceGridVisualizer>();
    }

    public void CalibrateZeroMulti(Quaternion rootRot, Quaternion midRot, Quaternion tipRot)
    {
        multiDriftBiases[0] = rootRot;
        multiDriftBiases[1] = midRot;
        multiDriftBiases[2] = tipRot;

        for (int i = 0; i < 3; i++)
        {
            multiPitches[i] = 0f;
            multiYaws[i] = 0f;
            multiLastRawPitches[i] = 0f;
            multiLastRawYaws[i] = 0f;
        }
    }

    public void UpdateMultiForceGrid(Vector3 rootForce, Vector3 midForce, Vector3 tipForce)
    {
        if (_forceGrids[0] != null) _forceGrids[0].UpdateForce(rootForce);
        if (_forceGrids[1] != null) _forceGrids[1].UpdateForce(midForce);
        if (_forceGrids[2] != null) _forceGrids[2].UpdateForce(tipForce);
    }

    public void SolveAndApplyMulti(Quaternion rootRot, Quaternion midRot, Quaternion tipRot, Vector3 bendAxis, Vector3 spreadAxis)
    {
        if (!rootBone || !midBone || !tipBone) return;

        if (PoseEditMode)
        {
            rootBone.localRotation = initialRoot * Quaternion.Euler(AdjRootX, AdjRootY, AdjRootZ);
            midBone.localRotation = initialMid * Quaternion.Euler(AdjMidX, AdjMidY, AdjMidZ);
            tipBone.localRotation = initialTip * Quaternion.Euler(AdjTipX, AdjTipY, AdjTipZ);
            return;
        }

        Quaternion compRoot = Quaternion.Inverse(multiDriftBiases[0]) * rootRot;
        Quaternion compMid = Quaternion.Inverse(multiDriftBiases[1]) * midRot;
        Quaternion compTip = Quaternion.Inverse(multiDriftBiases[2]) * tipRot;

        float rRawPitch, rRawYaw, mRawPitch, mRawYaw, tRawPitch, tRawYaw;
        if (isThumb)
        {
            ExtractThumbAngles(compRoot, bendAxis, spreadAxis, out rRawPitch, out rRawYaw);
            ExtractThumbAngles(compMid, bendAxis, spreadAxis, out mRawPitch, out mRawYaw);
            ExtractThumbAngles(compTip, bendAxis, spreadAxis, out tRawPitch, out tRawYaw);
        }
        else
        {
            ExtractFingerAngles(compRoot, bendAxis, spreadAxis, out rRawPitch, out rRawYaw);
            ExtractFingerAngles(compMid, bendAxis, spreadAxis, out mRawPitch, out mRawYaw);
            ExtractFingerAngles(compTip, bendAxis, spreadAxis, out tRawPitch, out tRawYaw);
        }

        // 🔥 修改点：分别传入三个节点的独立灵敏度
        ProcessMultiDelta(0, rRawPitch, rRawYaw, rootSensitivity);
        ProcessMultiDelta(1, mRawPitch, mRawYaw, midSensitivity);
        ProcessMultiDelta(2, tRawPitch, tRawYaw, tipSensitivity);

        float localRootPitch = multiPitches[0];
        float localMidPitch = multiPitches[1] - multiPitches[0];
        float localTipPitch = multiPitches[2] - multiPitches[1];
        float localRootYaw = multiYaws[0];

        localRootPitch = Mathf.Clamp(localRootPitch, minPitch, maxPitch);
        localMidPitch = Mathf.Clamp(localMidPitch, minPitch, maxPitch);
        localTipPitch = Mathf.Clamp(localTipPitch, minPitch, maxPitch);
        localRootYaw = Mathf.Clamp(localRootYaw, -maxYaw, maxYaw);

        // 🔥 消除死区核心：把截断后的真实模型角度，反向覆写给累加器 (Anti-Windup)
        multiPitches[0] = localRootPitch;
        multiPitches[1] = localRootPitch + localMidPitch;
        multiPitches[2] = localRootPitch + localMidPitch + localTipPitch;
        multiYaws[0] = localRootYaw;

        Vector3 trueSpreadAxis = Vector3.Cross(bendAxis, spreadAxis).normalized;
        if (trueSpreadAxis == Vector3.zero) trueSpreadAxis = Vector3.forward;

        if (isThumb)
        {
            Quaternion baseOffset = Quaternion.AngleAxis(thumbBaseYawOffset, spreadAxis);
            Vector3 locSpread = Quaternion.Inverse(initialRoot) * (baseOffset * spreadAxis);
            Vector3 locBend = Quaternion.Inverse(initialRoot) * (baseOffset * Vector3.up);

            rootBone.localRotation = initialRoot * Quaternion.AngleAxis(localRootYaw, locSpread) * Quaternion.AngleAxis(localRootPitch, locBend);
            midBone.localRotation = initialMid * Quaternion.AngleAxis(localMidPitch, locBend);
            tipBone.localRotation = initialTip * Quaternion.AngleAxis(localTipPitch, locBend);
        }
        else
        {
            rootBone.localRotation = initialRoot * Quaternion.AngleAxis(localRootYaw, trueSpreadAxis) * Quaternion.AngleAxis(localRootPitch, bendAxis);
            midBone.localRotation = initialMid * Quaternion.AngleAxis(localMidPitch, bendAxis);
            tipBone.localRotation = initialTip * Quaternion.AngleAxis(localTipPitch, bendAxis);
        }
    }

    // 🔥 修改点：增加一个 currentSensitivity 参数
    private void ProcessMultiDelta(int index, float rawPitch, float rawYaw, float currentSensitivity)
    {
        float dPitch = rawPitch - multiLastRawPitches[index];
        float dYaw = rawYaw - multiLastRawYaws[index];

        if (dPitch > 180f) dPitch -= 360f; if (dPitch < -180f) dPitch += 360f;
        if (dYaw > 180f) dYaw -= 360f; if (dYaw < -180f) dYaw += 360f;

        // 乘上各自传进来的独立灵敏度
        multiPitches[index] += dPitch * currentSensitivity;
        multiYaws[index] += dYaw * currentSensitivity;

        multiLastRawPitches[index] = rawPitch;
        multiLastRawYaws[index] = rawYaw;
    }

    private void ExtractThumbAngles(Quaternion compensatedRot, Vector3 bendAxis, Vector3 spreadAxis, out float pitch, out float yaw)
    {
        Quaternion baseOffset = Quaternion.AngleAxis(thumbBaseYawOffset, spreadAxis);
        Vector3 thumbTrueBendAxis = baseOffset * Vector3.up;
        Vector3 thumbTrueSpreadAxis = baseOffset * spreadAxis;
        Vector3 boneLongAxis = Vector3.Cross(thumbTrueSpreadAxis, thumbTrueBendAxis).normalized;
        if (boneLongAxis == Vector3.zero) boneLongAxis = Vector3.forward;

        Vector3 currentBoneDir = compensatedRot * boneLongAxis;
        Vector3 pitchProjected = Vector3.ProjectOnPlane(currentBoneDir, thumbTrueBendAxis);
        if (pitchProjected == Vector3.zero) pitchProjected = boneLongAxis;
        pitch = Vector3.SignedAngle(boneLongAxis, pitchProjected, thumbTrueBendAxis);

        Vector3 yawProjected = Vector3.ProjectOnPlane(currentBoneDir, thumbTrueSpreadAxis);
        if (yawProjected == Vector3.zero) yawProjected = boneLongAxis;
        yaw = Vector3.SignedAngle(boneLongAxis, yawProjected, thumbTrueSpreadAxis);
    }

    private void ExtractFingerAngles(Quaternion compensatedRot, Vector3 bendAxis, Vector3 spreadAxis, out float pitch, out float yaw)
    {
        Vector3 boneLongAxis = spreadAxis;
        Vector3 trueSpreadAxis = Vector3.Cross(bendAxis, boneLongAxis).normalized;
        if (trueSpreadAxis == Vector3.zero) trueSpreadAxis = Vector3.forward;

        Vector3 currentBoneDir = compensatedRot * boneLongAxis;
        Vector3 pitchProjected = Vector3.ProjectOnPlane(currentBoneDir, bendAxis);
        if (pitchProjected == Vector3.zero) pitchProjected = boneLongAxis;
        pitch = Vector3.SignedAngle(boneLongAxis, pitchProjected, bendAxis);

        Vector3 yawProjected = Vector3.ProjectOnPlane(currentBoneDir, trueSpreadAxis);
        if (yawProjected == Vector3.zero) yawProjected = boneLongAxis;
        yaw = Vector3.SignedAngle(boneLongAxis, yawProjected, trueSpreadAxis);
    }
}