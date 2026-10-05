using UnityEngine;
using System.Text;
using System.Collections.Generic;

public enum AxisMap { X, Y, Z, NegativeX, NegativeY, NegativeZ }

[RequireComponent(typeof(SerialReceiver))]
public class HandMotionManager : MonoBehaviour
{
    public bool IsCalibrated => isCalibrated;
    private SerialReceiver receiver;

    [Header("硬件与手掌配置")]
    public Transform wristBone;
    public Transform palmBone;
    [System.NonSerialized] public int palmId = 0x30;

    [Header("IMU -> Unity 坐标系映射")]
    public AxisMap imuXToUnity = AxisMap.X;
    public AxisMap imuYToUnity = AxisMap.Y;
    public AxisMap imuZToUnity = AxisMap.Z;

    [Header("👍 大拇指 IMU -> Unity 坐标系映射")]
    public AxisMap thumbImuXToUnity = AxisMap.X;
    public AxisMap thumbImuYToUnity = AxisMap.Y;
    public AxisMap thumbImuZToUnity = AxisMap.Z;

    [Header("统一手指关节轴向")]
    public Vector3 fingerBendAxis = Vector3.right;
    public Vector3 fingerSpreadAxis = Vector3.up;

    [Header("🔥 手掌防麻花限制 (Swing-Twist 分解)")]
    public Vector3 palmTwistAxis = Vector3.forward;
    [Range(0f, 180f)] public float palmMaxSwingAngle = 70f;
    [Range(0f, 180f)] public float palmMaxTwistAngle = 60f;

    [Header("交互配置")]
    public KeyCode calibrateKey = KeyCode.Space;
    public bool printDebugLog = true;
    [Range(0.1f, 2f)] public float printInterval = 0.5f;
    private float printTimer = 0f;

    // 🔥 构造函数更新：名字, 指根ID, 指中ID, 指尖ID, 是否拇指, 最小弯曲, 最大弯曲, 最大左右摆动
    [Header("--- 手指骨骼绑定 (当前仅测试食指) ---")]
    public FingerSolver thumb = new FingerSolver("Thumb", 0x00, 0x00, 0x00, true, -15f, 80f, 35f); // 暂不测试
    public FingerSolver index = new FingerSolver("Index", 0x1E, 0x1F, 0x20, false, -10f, 95f, 20f); // ✅ 专属测试：食指3节点
    public FingerSolver middle = new FingerSolver("Middle", 0x00, 0x00, 0x00, false, -10f, 100f, 15f); // 暂不测试
    public FingerSolver ring = new FingerSolver("Ring", 0x00, 0x00, 0x00, false, -10f, 100f, 15f); // 暂不测试
    public FingerSolver little = new FingerSolver("Little", 0x00, 0x00, 0x00, false, -15f, 90f, 25f);  // 暂不测试

    private Quaternion palmCalibration = Quaternion.identity;
    private Quaternion palmDriftBias = Quaternion.identity;
    private Quaternion initialPalmLocalRot;
    public bool isCalibrated = false;

    private FingerSolver[] fingersCache;

    void Start()
    {
        receiver = GetComponent<SerialReceiver>();
        if (palmBone) initialPalmLocalRot = palmBone.localRotation;

        fingersCache = new[] { thumb, index, middle, ring, little };
        foreach (var f in fingersCache) f.Init();

        foreach (var f in fingersCache) f.PoseEditMode = false;
        Debug.Log("✅ 纯净多传感器模式已启动，当前追踪食指 (1E, 1F, 20)");
    }

    void Update()
    {
        if (Input.GetKeyDown(calibrateKey)) Calibrate();

        if (printDebugLog)
        {
            printTimer += Time.deltaTime;
            if (printTimer >= printInterval)
            {
                printTimer = 0f;
                PrintHardwareStatusToConsole();
            }
        }

        if (!isCalibrated) return;

        if (receiver.ImuDataDict.TryGetValue(palmId, out Quaternion rawPalmQ))
        {
            Quaternion alignedPalmQ = MapCoordinates(rawPalmQ);
            Quaternion relativePalmRot = Quaternion.Inverse(palmCalibration) * alignedPalmQ;
            Quaternion compensatedPalmRot = Quaternion.Inverse(palmDriftBias) * relativePalmRot;

            DecomposeSwingTwist(compensatedPalmRot, palmTwistAxis, out Quaternion swing, out Quaternion twist);
            twist.ToAngleAxis(out float twistAngle, out Vector3 tAxis);
            if (twistAngle > 180f) twistAngle -= 360f;
            twistAngle = Mathf.Clamp(twistAngle, -palmMaxTwistAngle, palmMaxTwistAngle);
            Quaternion clampedTwist = Quaternion.AngleAxis(twistAngle, tAxis);

            swing.ToAngleAxis(out float swingAngle, out Vector3 sAxis);
            if (swingAngle > 180f) swingAngle -= 360f;
            swingAngle = Mathf.Clamp(swingAngle, -palmMaxSwingAngle, palmMaxSwingAngle);
            Quaternion clampedSwing = Quaternion.AngleAxis(swingAngle, sAxis);

            Quaternion clampedPalmRot = clampedSwing * clampedTwist;

            if (Quaternion.Angle(compensatedPalmRot, clampedPalmRot) > 1.0f)
            {
                Quaternion targetBias = relativePalmRot * Quaternion.Inverse(clampedPalmRot);
                float blendAlpha = 1f - Mathf.Exp(-8f * Time.deltaTime);
                palmDriftBias = Quaternion.Slerp(palmDriftBias, targetBias, blendAlpha);
            }

            if (palmBone) palmBone.localRotation = initialPalmLocalRot * clampedPalmRot;

            // 🔥 获取每个手指的3个IMU数据并解算
            foreach (var finger in fingersCache)
            {
                // 如果传感器不存在(比如测试阶段的其他手指)，就当它是直的(identity)
                Quaternion rawRoot = receiver.ImuDataDict.GetValueOrDefault(finger.rootId, Quaternion.identity);
                Quaternion rawMid = receiver.ImuDataDict.GetValueOrDefault(finger.midId, Quaternion.identity);
                Quaternion rawTip = receiver.ImuDataDict.GetValueOrDefault(finger.tipId, Quaternion.identity);

                Quaternion alignRoot = finger.isThumb ? MapThumbCoordinates(rawRoot) : MapCoordinates(rawRoot);
                Quaternion alignMid = finger.isThumb ? MapThumbCoordinates(rawMid) : MapCoordinates(rawMid);
                Quaternion alignTip = finger.isThumb ? MapThumbCoordinates(rawTip) : MapCoordinates(rawTip);

                Quaternion relRoot = Quaternion.Inverse(alignedPalmQ) * alignRoot;
                Quaternion relMid = Quaternion.Inverse(alignedPalmQ) * alignMid;
                Quaternion relTip = Quaternion.Inverse(alignedPalmQ) * alignTip;

                finger.SolveAndApplyMulti(relRoot, relMid, relTip, fingerBendAxis, fingerSpreadAxis);

                // 力反馈更新 (基于指尖ID读取压力)
                Vector3 rootForce = receiver.ForceDataDict.GetValueOrDefault(finger.rootId, Vector3.zero);
                Vector3 midForce = receiver.ForceDataDict.GetValueOrDefault(finger.midId, Vector3.zero);
                Vector3 tipForce = receiver.ForceDataDict.GetValueOrDefault(finger.tipId, Vector3.zero);

                // 同时推给模型
                finger.UpdateMultiForceGrid(rootForce, midForce, tipForce);

            }
        }
    }

    private void DecomposeSwingTwist(Quaternion q, Vector3 twistAxis, out Quaternion swing, out Quaternion twist)
    {
        Vector3 rotationAxis = new Vector3(q.x, q.y, q.z);
        Vector3 projection = Vector3.Project(rotationAxis, twistAxis);
        twist = new Quaternion(projection.x, projection.y, projection.z, q.w).normalized;
        swing = q * Quaternion.Inverse(twist);
    }

    [ContextMenu("一键标定校准 (Calibrate)")]
    public void Calibrate()
    {
        if (receiver.ImuDataDict.TryGetValue(palmId, out Quaternion rawPalmQ))
        {
            palmCalibration = MapCoordinates(rawPalmQ);
            palmDriftBias = Quaternion.identity;
        }

        foreach (var finger in fingersCache)
        {
            Quaternion rawRoot = receiver.ImuDataDict.GetValueOrDefault(finger.rootId, Quaternion.identity);
            Quaternion rawMid = receiver.ImuDataDict.GetValueOrDefault(finger.midId, Quaternion.identity);
            Quaternion rawTip = receiver.ImuDataDict.GetValueOrDefault(finger.tipId, Quaternion.identity);

            Quaternion alignRoot = finger.isThumb ? MapThumbCoordinates(rawRoot) : MapCoordinates(rawRoot);
            Quaternion alignMid = finger.isThumb ? MapThumbCoordinates(rawMid) : MapCoordinates(rawMid);
            Quaternion alignTip = finger.isThumb ? MapThumbCoordinates(rawTip) : MapCoordinates(rawTip);

            Quaternion palmAligned = receiver.ImuDataDict.ContainsKey(palmId) ? MapCoordinates(receiver.ImuDataDict[palmId]) : Quaternion.identity;

            Quaternion relRoot = Quaternion.Inverse(palmAligned) * alignRoot;
            Quaternion relMid = Quaternion.Inverse(palmAligned) * alignMid;
            Quaternion relTip = Quaternion.Inverse(palmAligned) * alignTip;

            finger.CalibrateZeroMulti(relRoot, relMid, relTip);
        }
        isCalibrated = true;
        Debug.Log("<color=#00FF00>✅ 动捕手套多节点校准完成！</color>");
    }

    private void PrintHardwareStatusToConsole()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<b><color=#00FF00>=== 多传感器动捕手套测试 ===</color></b>");
        sb.AppendLine(isCalibrated ? "<color=#00FF00>状态: 已校准跟踪中</color>" : "<color=#FF9900>状态: 未校准 (请按空格键)</color>");

        AppendNodeStatus(sb, "手掌 (Palm)", palmId);

        // 专门打印食指状态
        sb.AppendLine("<b>--- 食指 (Index) 硬件状态 ---</b>");
        AppendNodeStatus(sb, "指根 (Root)", index.rootId);
        AppendNodeStatus(sb, "指中 (Mid)", index.midId);
        AppendNodeStatus(sb, "指尖 (Tip)", index.tipId);

        Debug.Log(sb.ToString());
    }

    private void AppendNodeStatus(StringBuilder sb, string label, int id)
    {
        if (id == 0x00) return; // 忽略占位符

        if (receiver.ImuDataDict.TryGetValue(id, out Quaternion q))
        {
            sb.AppendLine($"<color=#55FFFF>[ON]</color> {label}(0x{id:X2}) | ({q.x:F2},{q.y:F2},{q.z:F2},{q.w:F2})");
        }
        else sb.AppendLine($"<color=#FF5555>[OFF]</color> {label}(0x{id:X2}) | 未收到报文");
    }

    private Quaternion MapCoordinates(Quaternion raw)
    {
        Vector3 iX = GetAxisVector(imuXToUnity);
        Vector3 iY = GetAxisVector(imuYToUnity);
        Vector3 iZ = GetAxisVector(imuZToUnity);

        if (Mathf.Abs(Vector3.Dot(iX, iY)) > 0.1f || Mathf.Abs(Vector3.Dot(iY, iZ)) > 0.1f) return raw;

        Vector3 mappedXYZ = iX * raw.x + iY * raw.y + iZ * raw.z;
        float determinant = Vector3.Dot(Vector3.Cross(iX, iY), iZ);
        float mappedW = determinant < 0f ? -raw.w : raw.w;

        return new Quaternion(mappedXYZ.x, mappedXYZ.y, mappedXYZ.z, mappedW);
    }

    private Quaternion MapThumbCoordinates(Quaternion raw)
    {
        Vector3 iX = GetAxisVector(thumbImuXToUnity);
        Vector3 iY = GetAxisVector(thumbImuYToUnity);
        Vector3 iZ = GetAxisVector(thumbImuZToUnity);

        if (Mathf.Abs(Vector3.Dot(iX, iY)) > 0.1f || Mathf.Abs(Vector3.Dot(iY, iZ)) > 0.1f) return raw;

        Vector3 mappedXYZ = iX * raw.x + iY * raw.y + iZ * raw.z;
        float determinant = Vector3.Dot(Vector3.Cross(iX, iY), iZ);
        float mappedW = determinant < 0f ? -raw.w : raw.w;

        return new Quaternion(mappedXYZ.x, mappedXYZ.y, mappedXYZ.z, mappedW);
    }

    private Vector3 GetAxisVector(AxisMap map)
    {
        return map switch
        {
            AxisMap.X => Vector3.right,
            AxisMap.Y => Vector3.up,
            AxisMap.Z => Vector3.forward,
            AxisMap.NegativeX => Vector3.left,
            AxisMap.NegativeY => Vector3.down,
            AxisMap.NegativeZ => Vector3.back,
            _ => Vector3.zero
        };
    }
}