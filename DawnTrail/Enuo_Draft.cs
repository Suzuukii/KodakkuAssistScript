using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Newtonsoft.Json;
using KodakkuAssist.Script;
using KodakkuAssist.Module.GameEvent;
using KodakkuAssist.Module.Draw;
using KodakkuAssist.Extensions;

namespace MmwEnuoDraftNamespace;

[ScriptType(
    name: "恩欧歼殛战 MMW攻略草稿",
    territorys: [1362],
    guid: "aa58398a-5048-4ca8-9b87-e5233e9a4e63",
    version: "0.0.1.0",
    author: "Suzuukii",
    note: "mmw文档+NOCCHH")]
public class MmwEnuoDraft
{
    #region Settings

    [UserSetting("通用 启用屏幕文字提示")]
    public bool EnableText { get; set; } = true;

    [UserSetting("通用 启用TTS")]
    public bool EnableTts { get; set; } = true;

    [UserSetting("调试 输出/e日志")]
    public bool EnableDebug { get; set; } = true;

    [UserSetting("通用 危险范围颜色")]
    public ScriptColor DangerColor { get; set; } = new() { V4 = new Vector4(1f, 0f, 0f, 1f) };

    [UserSetting("通用 安全/指路颜色")]
    public ScriptColor SafeColor { get; set; } = new() { V4 = new Vector4(0f, 1f, 1f, 1f) };

    [UserSetting("通用 分摊/塔颜色")]
    public ScriptColor StackColor { get; set; } = new() { V4 = new Vector4(0f, 0.55f, 1f, 1f) };

    #endregion

    #region State

    private static readonly Vector3 Center = new(100f, 0f, 100f);
    private static readonly Vector3 North = new(100f, 0f, 86f);
    private static readonly Vector3 East = new(114f, 0f, 100f);
    private static readonly Vector3 South = new(100f, 0f, 114f);
    private static readonly Vector3 West = new(86f, 0f, 100f);

    private int _phase = 1;
    private int _expansionCount = 0;
    private int _waveCount = 0;
    private int _zeroDimensionalCount = 0;
    private int? _firstTargetIcon;
    private readonly object _stateLock = new();

    private readonly HashSet<uint> _spreadTargets = new();
    private readonly HashSet<uint> _stackTargets = new();
    private readonly List<(uint Source, uint Target, string Id)> _tethers = new();
    private readonly List<(Vector3 Position, uint SourceId)> _chaosOrbOrder = new();
    private readonly List<(int OrbOrder, Vector3 Position, uint SourceId)> _chaosYellowLinks = new();
    private readonly List<(int OrbOrder, Vector3 Position, uint SourceId)> _chaosPurpleLinks = new();
    private int _chaosOrbSpawnCount = 0;
    private bool _chaosYellowAssigned = false;
    private bool _chaosPurpleAssigned = false;

    private readonly bool[] _p2TowerExists = new bool[8];
    private readonly Vector3[] _p2TowerPositions = new Vector3[8];
    private readonly List<int> _p2FanTargetIndexes = new();
    private int _p2TowerCount = 0;
    private bool _p2Assigned = false;
    private uint _p2BossId = 0;
    private readonly HashSet<uint> _trackingFireTargets = new();
    private int _trackingFireWave = 0;

    private static readonly Vector3[] P2Slots =
    [
        new Vector3(109.54f, -0.02f, 76.89f),
        new Vector3(123.09f, -0.02f, 90.41f),
        new Vector3(123.09f, -0.02f, 109.54f),
        new Vector3(109.54f, -0.02f, 123.09f),
        new Vector3(90.41f, -0.02f, 123.09f),
        new Vector3(76.89f, -0.02f, 109.54f),
        new Vector3(76.89f, -0.02f, 90.41f),
        new Vector3(90.41f, -0.02f, 76.89f),
    ];

    #endregion

    #region Lifecycle And Debug

    public void Init(ScriptAccessory accessory)
    {
        accessory.Method.RemoveDraw(".*");
        _phase = 1;
        _expansionCount = 0;
        _waveCount = 0;
        _zeroDimensionalCount = 0;
        _firstTargetIcon = null;
        _spreadTargets.Clear();
        _stackTargets.Clear();
        _tethers.Clear();
        ResetChaosCurrent();
        ResetP2VoidVortex();
        _trackingFireTargets.Clear();
        _trackingFireWave = 0;
        Debug(accessory, "Initialized.");
    }

    [ScriptMethod(name: "Debug 清除绘图", eventType: EventTypeEnum.Chat, eventCondition: ["Type:Echo", "Message:MMWCLEAR"], userControl: false)]
    public void DebugClear(Event @event, ScriptAccessory accessory)
    {
        accessory.Method.RemoveDraw("MMW_.*");
        Debug(accessory, "Clear MMW drawings.");
    }

    [ScriptMethod(name: "Debug 显示阶段", eventType: EventTypeEnum.Chat, eventCondition: ["Type:Echo", "Message:MMWPHASE"], userControl: false)]
    public void DebugShowPhase(Event @event, ScriptAccessory accessory)
    {
        Debug(accessory, $"P{_phase}, expansion={_expansionCount}, wave={_waveCount}, zero={_zeroDimensionalCount}");
    }

    [ScriptMethod(name: "Debug 设置P1", eventType: EventTypeEnum.Chat, eventCondition: ["Type:Echo", "Message:MMWP1"], userControl: false)]
    public void DebugSetP1(Event @event, ScriptAccessory accessory) => SetPhase(1, accessory);

    [ScriptMethod(name: "Debug 设置P2", eventType: EventTypeEnum.Chat, eventCondition: ["Type:Echo", "Message:MMWP2"], userControl: false)]
    public void DebugSetP2(Event @event, ScriptAccessory accessory) => SetPhase(2, accessory);

    [ScriptMethod(name: "Debug 设置P3", eventType: EventTypeEnum.Chat, eventCondition: ["Type:Echo", "Message:MMWP3"], userControl: false)]
    public void DebugSetP3(Event @event, ScriptAccessory accessory) => SetPhase(3, accessory);

    private void SetPhase(int phase, ScriptAccessory accessory)
    {
        _phase = phase;
        _waveCount = 0;
        _zeroDimensionalCount = 0;
        _spreadTargets.Clear();
        _stackTargets.Clear();
        _tethers.Clear();
        ResetChaosCurrent();
        ResetP2VoidVortex();
        _trackingFireTargets.Clear();
        _trackingFireWave = 0;
        accessory.Method.RemoveDraw("MMW_.*");
        Debug(accessory, $"Set phase P{_phase}.");
    }

    #endregion

    #region Phase Control

    [ScriptMethod(name: "P1 无之领域 转P2", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:50010"], userControl: false)]
    public void P1ToP2(Event @event, ScriptAccessory accessory)
    {
        Text(accessory, "转场：无之领域", 5000, true);
        Tts(accessory, "转场");
        SetPhase(2, accessory);
    }

    [ScriptMethod(name: "P2 结束 转P3", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:50029"], userControl: false)]
    public void P2ToP3(Event @event, ScriptAccessory accessory)
    {
        Text(accessory, "进入P3", 5000, true);
        Tts(accessory, "进入P3");
        SetPhase(3, accessory);
    }

    #endregion

    #region Raidwide

    [ScriptMethod(name: "流星雨 / 至高无上", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:regex:^(PLACEHOLDER_METEOR_RAIN|PLACEHOLDER_SUPREMACY|PLACEHOLDER_LIGHTLESS_WORLD)$"])]
    public void Raidwide(Event @event, ScriptAccessory accessory)
    {
        var aid = @event["ActionId"];
        var text = aid switch
        {
            "PLACEHOLDER_LIGHTLESS_WORLD" => "六段AOE，注意减伤",
            "PLACEHOLDER_SUPREMACY" => "至高无上，全屏AOE",
            _ => "流星雨，全屏AOE"
        };

        Text(accessory, text, 5000, true);
        Tts(accessory, text);
    }

    #endregion

    #region P1 Void Expansion

    [ScriptMethod(name: "通用 无之膨胀 钢铁/月环", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:regex:^(49977|49978|49979|49980)$"])]
    public void VoidExpansion(Event @event, ScriptAccessory accessory)
    {
        if (!ParseObjectId(@event["SourceId"], out var sid)) return;

        _expansionCount++;
        var duration = 7700;
        var aid = @event["ActionId"];

        switch (aid)
        {
            case "49977":
                DrawCircle(accessory, $"MMW_无之膨胀_大钢铁_{_expansionCount}", sid, 40f, duration, DangerColor.V4);
                Text(accessory, "大黑洞钢铁：远离黑洞，处理挡枪", duration, true);
                Tts(accessory, "大钢铁远离");
                break;
            case "49978":
                DrawDonut(accessory, $"MMW_无之膨胀_大月环_{_expansionCount}", sid, 60f, 40f, duration, DangerColor.V4);
                Text(accessory, "大黑洞月环：靠近黑洞，处理挡枪", duration, true);
                Tts(accessory, "大月环靠近");
                break;
            case "49979":
                DrawCircle(accessory, $"MMW_无之膨胀_本体钢铁_{_expansionCount}", sid, 12f, duration, DangerColor.V4);
                Text(accessory, "本体钢铁：远离本体", duration, true);
                Tts(accessory, "本体钢铁");
                break;
            case "49980":
                DrawDonut(accessory, $"MMW_无之膨胀_本体月环_{_expansionCount}", sid, 40f, 6f, duration, DangerColor.V4);
                Text(accessory, "本体月环：靠近本体", duration, true);
                Tts(accessory, "本体月环");
                break;
        }
    }

    [ScriptMethod(name: "通用 回归重波动 单黑球挡枪", eventType: EventTypeEnum.TargetIcon, eventCondition: ["Id:02BE"])]
    public void SingleBlackBall(Event @event, ScriptAccessory accessory)
    {
        if (!ParseObjectId(@event["TargetId"], out var target)) return;
        var orb = @event.SourceId();
        if (orb == 0) return;

        DrawRectToTarget(accessory, $"MMW_单黑球挡枪_{orb:X}_{target:X}", orb, target, 6f, 15f, 9500, SafeColor.V4);
        Text(accessory, "单黑球：T挡枪，人群靠近BOSS", 6000, true);
    }

    [ScriptMethod(name: "通用 回归波动 双黑球挡枪", eventType: EventTypeEnum.TargetIcon, eventCondition: ["Id:02BD"])]
    public void DoubleBlackBall(Event @event, ScriptAccessory accessory)
    {
        var orb = @event.SourceId();
        var target = @event.TargetId();
        var targetIndex = accessory.Data.PartyList.IndexOf(target);
        var myIndex = accessory.MyIndex();
        if (orb == 0 || !IsValidPartyIndex(targetIndex) || !IsValidPartyIndex(myIndex)) return;

        var sameParity = myIndex % 2 == targetIndex % 2;
        DrawRectToTarget(accessory, $"MMW_双黑球挡枪_{targetIndex}", orb, target, 6f, 15f, 9500,
            sameParity ? SafeColor.V4 : DangerColor.V4);
    }

    [ScriptMethod(name: "P1 无之活性 黑洞移动", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:PLACEHOLDER_P1_VOID_ACTIVATION"], userControl: false)]
    public void P1VoidActivation(Event @event, ScriptAccessory accessory)
    {
        Text(accessory, "黑洞位置调整，观察新12点", 5000, false);
        Tts(accessory, "黑洞移动");
    }

    #endregion

    #region P1 Core Meltdown

    [ScriptMethod(name: "核心熔毁 集合停手", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:50040"])]
    public void CoreMeltdownStart(Event @event, ScriptAccessory accessory)
    {
        var duration = EventDuration(@event, 8000);
        DrawWaypoint(accessory, "MMW_核心熔毁_集合", Center, duration);
        Text(accessory, "BOSS脚下集合，烈焰锢期间停止移动", duration, true);
        Tts(accessory, "集合停手");
    }

    [ScriptMethod(name: "核心熔毁 烈焰锢消失记录", eventType: EventTypeEnum.StatusRemove, eventCondition: ["StatusID:PLACEHOLDER_FLAME_BIND_STATUS"], userControl: false)]
    public void CoreMeltdownStatusRemove(Event @event, ScriptAccessory accessory)
    {
        if (!ParseObjectId(@event["TargetId"], out var target)) return;

        var pos = EventVector(@event, "TargetPosition", Center);
        DrawCircle(accessory, $"MMW_核心熔毁_原地AOE_{target:X}", pos, 5f, 3500, DangerColor.V4);
    }

    [ScriptMethod(name: "核心熔毁 延迟分散", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:50040"], userControl: false)]
    public void CoreMeltdownSpread(Event @event, ScriptAccessory accessory)
    {
        var duration = EventDuration(@event, 5000);
        var myPos = StandardSpreadPosition(accessory.MyIndex());
        _ = Task.Run(async () =>
        {
            await Task.Delay(7500);
            DrawWaypoint(accessory, "MMW_核心熔毁_八方分散", myPos, 3300);
            DrawCircle(accessory, "MMW_核心熔毁_自身分散圈", accessory.Data.Me, 5f, 3300, DangerColor.V4);
            Text(accessory, "标准八方分散", 3300, true);
            Tts(accessory, "八方分散");
        });
    }

    #endregion

    #region P1 Wave Stack

    [ScriptMethod(name: "集束/扩散波动", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:regex:^(50033|50032)$"])]
    public void FocusDiffuseWave(Event @event, ScriptAccessory accessory)
    {
        _waveCount++;
        if (!ParseObjectId(@event["SourceId"], out var sid)) return;

        const int duration = 4800;
        var aid = @event["ActionId"];
        var myIndex = accessory.MyIndex();
        if (!IsValidPartyIndex(myIndex)) return;

        if (aid == "50033")
        {
            DrawFanToPartyIndex(accessory, "MMW_集束_左组扇", sid, 2, 60f, MathF.PI * 10f / 18f, duration,
                IsEvenGroup(myIndex) ? SafeColor.V4 : DangerColor.V4);
            DrawFanToPartyIndex(accessory, "MMW_集束_右组扇", sid, 3, 60f, MathF.PI * 10f / 18f, duration,
                IsOddGroup(myIndex) ? SafeColor.V4 : DangerColor.V4);
            DrawWaypoint(accessory, "MMW_集束_我的分摊点", IsEvenGroup(myIndex) ? West : East, duration);
            Text(accessory, "集束波动：MT组D点，ST组B点", duration, true);
            Tts(accessory, "四四分摊");
        }
        else
        {
            var pos = PairStackPosition(myIndex);
            DrawWaypoint(accessory, "MMW_扩散_我的二人分摊", pos, duration);
            var safeFanIndex = myIndex switch
            {
                0 or 6 => 0,
                1 or 5 => 1,
                2 or 4 => 2,
                3 or 7 => 3,
                _ => -1
            };
            for (var i = 0; i < 4; i++)
            {
                DrawFanToPartyIndex(accessory, $"MMW_扩散_二人扇_{i}", sid, i, 60f, MathF.PI / 3f, duration,
                    i == safeFanIndex ? SafeColor.V4 : DangerColor.V4);
            }
            Text(accessory, "扩散波动：标准八方同色二二分摊", duration, true);
            Tts(accessory, "二二分摊");
        }
    }

    #endregion

    #region P1 Chaos Current

    [ScriptMethod(name: "混沌激流 旋转球起点", eventType: EventTypeEnum.AddCombatant, eventCondition: ["DataId:regex:^(19909|19910)$"], userControl: false)]
    public void ChaosCurrentOrbSpawn(Event @event, ScriptAccessory accessory)
    {
        var pos = EventVector(@event, "SourcePosition", Center);
        var sourceId = @event.SourceId();
        var dataId = @event.DataId();

        lock (_stateLock)
        {
            if (dataId == 19909 && _chaosOrbSpawnCount == 0)
            {
                ResetChaosCurrent();
            }

            _chaosOrbSpawnCount++;
            if (dataId == 19909 && _chaosOrbSpawnCount <= 8)
            {
                _chaosOrbOrder.Add((pos, sourceId));
            }
            else if (dataId == 19910)
            {
                var replaceIndex = _chaosOrbSpawnCount switch
                {
                    9 => 0,
                    10 => 1,
                    _ => -1
                };

                if (replaceIndex >= 0 && replaceIndex < _chaosOrbOrder.Count)
                {
                    _chaosOrbOrder[replaceIndex] = (pos, sourceId);
                }
            }
        }

        DrawFanFromCenter(accessory, $"MMW_混沌激流_旋转球扇_{sourceId:X}", pos, 45f, 30f, 7000, DangerColor.V4);
        Text(accessory, "旋转球：8穿1，然后跟判定走", 6000, true);
    }

    [ScriptMethod(name: "混沌激流 撞球记录", eventType: EventTypeEnum.Tether, eventCondition: ["Id:regex:^(0196|0197)$"], userControl: false)]
    public void ChaosCurrentBallTether(Event @event, ScriptAccessory accessory)
    {
        var myIndex = accessory.MyIndex();
        if (!IsValidPartyIndex(myIndex)) return;

        var isYellow = NormalizeHex(@event["Id"]) == "0196";
        var sourcePos = EventVector(@event, "SourcePosition", Center);
        var myTypeOrder = myIndex / 2;

        Vector3 targetPos;
        uint targetObject;
        bool shouldDraw = false;

        lock (_stateLock)
        {
            if (_chaosOrbOrder.Count == 0) return;

            var orbIndex = FindNearestChaosOrb(sourcePos);
            if (orbIndex < 0) return;

            var targetList = isYellow ? _chaosYellowLinks : _chaosPurpleLinks;
            var alreadyAssigned = isYellow ? _chaosYellowAssigned : _chaosPurpleAssigned;
            if (alreadyAssigned || targetList.Any(x => x.OrbOrder == orbIndex)) return;

            var orb = _chaosOrbOrder[orbIndex];
            targetList.Add((orbIndex, orb.Position, orb.SourceId));
            if (targetList.Count < 4) return;

            var priority = GetChaosClockwisePriority();
            var rank = priority.Select((order, i) => new { order, i }).ToDictionary(x => x.order, x => x.i);
            var sorted = targetList
                .OrderBy(x => rank.TryGetValue(x.OrbOrder, out var r) ? r : 999)
                .ToList();

            if (myTypeOrder >= sorted.Count) return;

            var myTarget = sorted[myTypeOrder];
            targetPos = myTarget.Position;
            targetObject = myTarget.SourceId;
            shouldDraw = targetObject != 0;

            if (isYellow) _chaosYellowAssigned = true;
            else _chaosPurpleAssigned = true;
        }

        if (!shouldDraw) return;

        var delay = isYellow ? 6000 : 1000;
        var duration = isYellow ? 6000 : 5000;
        DrawWaypointToObject(accessory, $"MMW_混沌激流_撞{(isYellow ? "黄" : "紫")}球_{myTypeOrder}", targetObject, duration, delay);
        DrawCircle(accessory, $"MMW_混沌激流_目标球_{targetObject:X}", targetPos, 4f, duration + delay, StackColor.V4);
        Text(accessory, isYellow ? "先撞黄球" : "后撞紫球，等易伤", duration, true);
    }

    #endregion

    #region P1 Vortex And Deep Freeze

    [ScriptMethod(name: "奔流 小球钢铁", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:regex:^(49995|49996|49997)$"])]
    public void Torrent(Event @event, ScriptAccessory accessory)
    {
        if (!ParseObjectId(@event["SourceId"], out var sid)) return;
        DrawCircle(accessory, "MMW_奔流_小球钢铁", sid, 7f, 6000, DangerColor.V4);
    }

    [ScriptMethod(name: "撞球易伤提示", eventType: EventTypeEnum.StatusAdd, eventCondition: ["StatusID:2941"])]
    public void BallVulnerability(Event @event, ScriptAccessory accessory)
    {
        if (@event.TargetId() != accessory.Data.Me) return;
        Text(accessory, "撞球易伤，等消失再撞下一颗", 3000, true);
        Tts(accessory, "等易伤");
    }

    [ScriptMethod(name: "无之漩涡 黑洞冲锋", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:PLACEHOLDER_VOID_VORTEX_CHARGE"])]
    public void VoidVortex(Event @event, ScriptAccessory accessory)
    {
        if (!ParseObjectId(@event["SourceId"], out var sid)) return;
        var duration = EventDuration(@event, 6000);

        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = "MMW_无之漩涡_冲锋路径";
        dp.Owner = sid;
        dp.Scale = new Vector2(8f, 40f);
        dp.Color = DangerColor.V4;
        dp.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Default, DrawTypeEnum.Rect, dp);

        Text(accessory, "黑洞沿弧条冲锋，躲路径和终点钢铁", duration, true);
    }

    [ScriptMethod(name: "深度冻结", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:50044"])]
    public void DeepFreeze(Event @event, ScriptAccessory accessory)
    {
        var duration = EventDuration(@event, 9000);
        var idx = accessory.MyIndex();
        var pos = idx switch
        {
            0 => new Vector3(88f, 0f, 88f),
            1 => new Vector3(112f, 0f, 88f),
            _ => South
        };

        DrawWaypoint(accessory, "MMW_深度冻结_站位", pos, duration);
        DrawCircle(accessory, "MMW_深度冻结_MT核爆", new Vector3(88f, 0f, 88f), 8f, duration, DangerColor.V4);
        DrawCircle(accessory, "MMW_深度冻结_ST核爆", new Vector3(112f, 0f, 88f), 8f, duration, DangerColor.V4);
        Text(accessory, "双T左右出，人群C点；冷却期间保持移动", duration, true);
        Tts(accessory, "双T出人群C点，保持移动");
    }

    #endregion

    #region P2 Small World

    [ScriptMethod(name: "P2 蓝圈击退", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:49369"])]
    public void P2Knockback(Event @event, ScriptAccessory accessory)
    {
        var duration = EventDuration(@event, 8000);
        var target = IsMtGroup(accessory.MyIndex()) ? new Vector3(86f, 0f, 100f) : new Vector3(114f, 0f, 100f);
        DrawWaypoint(accessory, "MMW_P2_NO_CCHH_东西击退", target, duration);
        DrawCircle(accessory, "MMW_P2_蓝圈即死", Center, 8f, duration, DangerColor.V4);
        Text(accessory, "P2：MT组西，ST组东；蓝圈内即死", duration, true);
        Tts(accessory, IsMtGroup(accessory.MyIndex()) ? "西侧" : "东侧");
    }

    [ScriptMethod(name: "P2 Boss记录", eventType: EventTypeEnum.AddCombatant, eventCondition: ["DataId:19911"], userControl: false)]
    public void P2BossRecord(Event @event, ScriptAccessory accessory)
    {
        if (_phase != 2) return;
        _p2BossId = @event.SourceId();
    }

    [ScriptMethod(name: "P2 塔记录", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:50013"], userControl: false)]
    public void P2TowerSpawn(Event @event, ScriptAccessory accessory)
    {
        if (_phase != 2) return;
        var pos = EventVector(@event, "SourcePosition", Center);
        var slot = GetP2SlotIndex(pos);
        if (slot < 0) return;

        lock (_stateLock)
        {
            if (!_p2TowerExists[slot])
            {
                _p2TowerExists[slot] = true;
                _p2TowerPositions[slot] = pos;
                _p2TowerCount++;
            }
        }

        DrawCircle(accessory, $"MMW_P2_塔_{slot}", pos, 3f, 10000, StackColor.V4);
        TryAssignP2VoidVortex(accessory);
    }

    [ScriptMethod(name: "P2 扇形点名记录", eventType: EventTypeEnum.TargetIcon, eventCondition: ["Id:02D1"], userControl: false)]
    public void P2FanTarget(Event @event, ScriptAccessory accessory)
    {
        if (_phase != 2) return;
        if (!ParseObjectId(@event["TargetId"], out var target)) return;
        var targetIndex = accessory.Data.PartyList.IndexOf(target);
        if (!IsValidPartyIndex(targetIndex)) return;

        lock (_stateLock)
        {
            if (!_p2FanTargetIndexes.Contains(targetIndex))
            {
                _p2FanTargetIndexes.Add(targetIndex);
            }
        }

        DrawFanFromPositionToTarget(accessory, $"MMW_P2_点名扇_{targetIndex}", Center, target, 60f, MathF.PI / 3f, 8000, DangerColor.V4);
        TryAssignP2VoidVortex(accessory);
    }

    [ScriptMethod(name: "P2 塔/扇形处理兜底", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:50013"])]
    public void P2TowerFanResolve(Event @event, ScriptAccessory accessory)
    {
        if (_phase != 2) return;
        TryAssignP2VoidVortex(accessory);
    }

    [ScriptMethod(name: "P2 小怪注意事项", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:regex:^(50022|PLACEHOLDER_ADD_INTERRUPT|PLACEHOLDER_ADD_TANKBUSTER|PLACEHOLDER_ADD_DISPEL)$"])]
    public void P2Adds(Event @event, ScriptAccessory accessory)
    {
        var aid = @event["ActionId"];
        var text = aid switch
        {
            "50022" => "D怪：恶魔之瞳，背对",
            "PLACEHOLDER_ADD_INTERRUPT" => "T怪：吸血触，插言",
            "PLACEHOLDER_ADD_TANKBUSTER" => "T怪：直线死刑向外引导",
            "PLACEHOLDER_ADD_DISPEL" => "H怪：疫病之触，驱散",
            _ => "小怪机制"
        };

        Text(accessory, text, 5000, true);
        Tts(accessory, text);
    }

    #endregion

    #region P3 Enhanced Expansion And Active

    [ScriptMethod(name: "P3 聚能波动 辣翅/辣尾", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:regex:^(49985|49986)$"], userControl: false)]
    public void P3ConcentratedWave(Event @event, ScriptAccessory accessory)
    {
        if (_phase != 3) return;

        var actionId = @event.ActionId();
        var duration = actionId == 49985 ? 6300 : 5700;
        var sourcePos = EventVector(@event, "SourcePosition", Center);
        var sourceRot = EventFloat(@event, "SourceRotation", 0f);

        if (actionId == 49985)
        {
            DrawRect(accessory, "MMW_P3_聚能波动_中线", sourcePos, new Vector2(16f, 80f), sourceRot, duration, DangerColor.V4);
            Text(accessory, "辣尾：中线危险", duration, true);
        }
        else
        {
            DrawRect(accessory, "MMW_P3_聚能波动_左翼", sourcePos, new Vector2(16f, 80f), sourceRot + MathF.PI / 2f, duration, DangerColor.V4);
            DrawRect(accessory, "MMW_P3_聚能波动_右翼", sourcePos, new Vector2(16f, 80f), sourceRot - MathF.PI / 2f, duration, DangerColor.V4);
            Text(accessory, "辣翅：两侧危险", duration, true);
        }
    }

    [ScriptMethod(name: "P3 无之膨胀 强化版", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:regex:^(49979|49980)$"])]
    public void P3EnhancedExpansion(Event @event, ScriptAccessory accessory)
    {
        if (_phase != 3) return;
        if (!ParseObjectId(@event["SourceId"], out var sid)) return;

        var duration = EventDuration(@event, 8000);
        var bossIron = @event["ActionId"] == "49979";

        if (bossIron)
        {
            DrawCircle(accessory, "MMW_P3_BOSS钢铁", sid, 12f, duration, DangerColor.V4);
            DrawWaypoint(accessory, "MMW_P3_钢铁靠12点场边", North, duration);
            Text(accessory, "P3强化膨胀：BOSS钢铁，靠12点场边处理分摊", duration, true);
            Tts(accessory, "本体钢铁靠十二点");
        }
        else
        {
            DrawDonut(accessory, "MMW_P3_BOSS月环", sid, 60f, 8f, duration, DangerColor.V4);
            DrawWaypoint(accessory, "MMW_P3_月环贴BOSS远离12点", South, duration);
            Text(accessory, "P3强化膨胀：BOSS月环，贴BOSS远离12点", duration, true);
            Tts(accessory, "本体月环远离十二点");
        }
    }

    [ScriptMethod(name: "P3 无之活性 辣翅/辣尾 占位", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:regex:^(PLACEHOLDER_P3_LINE_SINGLE|PLACEHOLDER_P3_LINE_DOUBLE)$"])]
    public void P3VoidActivationLines(Event @event, ScriptAccessory accessory)
    {
        if (_phase != 3) return;

        var duration = EventDuration(@event, 7000);
        var single = @event["ActionId"] == "PLACEHOLDER_P3_LINE_SINGLE";

        if (single)
        {
            DrawRect(accessory, "MMW_P3_辣尾中线", Center, new Vector2(10f, 40f), 0f, duration, DangerColor.V4);
            Text(accessory, "大黑洞：辣尾，中线危险", duration, true);
        }
        else
        {
            DrawRect(accessory, "MMW_P3_辣翅左", new Vector3(92f, 0f, 100f), new Vector2(8f, 40f), 0f, duration, DangerColor.V4);
            DrawRect(accessory, "MMW_P3_辣翅右", new Vector3(108f, 0f, 100f), new Vector2(8f, 40f), 0f, duration, DangerColor.V4);
            Text(accessory, "小黑洞：辣翅，两侧危险", duration, true);
        }
    }

    #endregion

    #region P3 Stacks And Zero Dimensional

    [ScriptMethod(name: "P3 暗影神圣 双奶分摊", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:50045"])]
    public void P3ShadowHoly(Event @event, ScriptAccessory accessory)
    {
        var duration = EventDuration(@event, 7000);
        DrawCircle(accessory, "MMW_P3_暗影神圣_MT组D", West, 5f, duration, StackColor.V4);
        DrawCircle(accessory, "MMW_P3_暗影神圣_ST组B", East, 5f, duration, StackColor.V4);
        DrawWaypoint(accessory, "MMW_P3_暗影神圣_我的点", IsMtGroup(accessory.MyIndex()) ? West : East, duration);
        Text(accessory, "暗影神圣：MT组D，ST组B", duration, true);
        Tts(accessory, "分组分摊");
    }

    [ScriptMethod(name: "P3 零次元 连续分摊", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:PLACEHOLDER_ZERO_DIMENSIONAL"])]
    public void P3ZeroDimensional(Event @event, ScriptAccessory accessory)
    {
        if (_phase != 3) return;

        _zeroDimensionalCount++;
        var duration = EventDuration(@event, 5000);
        var times = 2 + _zeroDimensionalCount;

        DrawCircle(accessory, $"MMW_P3_零次元_{_zeroDimensionalCount}", Center, 5f, duration * times, StackColor.V4);
        DrawWaypoint(accessory, "MMW_P3_零次元_集合", Center, duration * times);
        Text(accessory, $"零次元：连续{times}次分摊", duration * times, true);
        Tts(accessory, $"{times}次分摊");
    }

    #endregion

    #region P3 Vortex Plus Core And Tracking Fire

    [ScriptMethod(name: "P3 无之漩涡+核心熔毁", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:PLACEHOLDER_P3_VORTEX_CORE"])]
    public void P3VortexCore(Event @event, ScriptAccessory accessory)
    {
        if (_phase != 3) return;

        var duration = EventDuration(@event, 12000);
        var pos = StandardSpreadPosition(accessory.MyIndex());
        DrawWaypoint(accessory, "MMW_P3_漩涡核心_八方前后移动", pos, duration);
        DrawCircle(accessory, "MMW_P3_自身分散", accessory.Data.Me, 6f, duration, DangerColor.V4);
        Text(accessory, "先八方前后躲小球；热病结束后继续分散移动", duration, true);
        Tts(accessory, "八方移动分散");
    }

    [ScriptMethod(name: "P3 追踪地火 连线记录", eventType: EventTypeEnum.Tether, eventCondition: ["Id:PLACEHOLDER_TRACKING_FIRE_TETHER"], userControl: false)]
    public void P3TrackingFireRecord(Event @event, ScriptAccessory accessory)
    {
        if (_phase != 3) return;

        if (!ParseObjectId(@event["TargetId"], out var target)) return;
        _trackingFireTargets.Add(target);
        Debug(accessory, $"Tracking fire target {target:X}");
    }

    [ScriptMethod(name: "P3 追踪地火 指路", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:PLACEHOLDER_TRACKING_FIRE_START"])]
    public void P3TrackingFireGuidance(Event @event, ScriptAccessory accessory)
    {
        if (_phase != 3) return;

        _trackingFireWave++;
        var duration = EventDuration(@event, 10000);
        var idx = accessory.MyIndex();
        var first = TrackingFirePosition(idx, step: 0);
        var second = TrackingFirePosition(idx, step: 1);

        if (_trackingFireTargets.Contains(accessory.Data.Me))
        {
            var start = _trackingFireWave == 1 ? first : second;
            DrawWaypoint(accessory, $"MMW_P3_追踪地火_第{_trackingFireWave}轮起点", start, duration);
            Text(accessory, _trackingFireWave == 1 ? "第一轮地火：靠近自己连线黑洞，顺时针引导" : "第二轮地火：接力后继续顺时针引导", duration, true);
            Tts(accessory, _trackingFireWave == 1 ? "第一轮顺时针引导" : "第二轮接力");
        }
        else
        {
            DrawWaypoint(accessory, "MMW_P3_追踪地火_非点名场中待机", Center, duration);
            Text(accessory, _trackingFireWave == 1 ? "非点名：场中躲双辣翅，准备第二轮接力" : "非点名：场中待机，准备最后一轮场外直线", duration, false);
        }
    }

    #endregion

    #region Draw Helpers

    private void DrawCircle(ScriptAccessory accessory, string name, uint owner, float radius, int duration, Vector4 color)
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Owner = owner;
        dp.Scale = new Vector2(radius);
        dp.Color = color;
        dp.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Default, DrawTypeEnum.Circle, dp);
    }

    private void DrawCircle(ScriptAccessory accessory, string name, Vector3 position, float radius, int duration, Vector4 color)
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Position = position;
        dp.Scale = new Vector2(radius);
        dp.Color = color;
        dp.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Default, DrawTypeEnum.Circle, dp);
    }

    private void DrawDonut(ScriptAccessory accessory, string name, uint owner, float outerRadius, float innerRadius, int duration, Vector4 color)
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Owner = owner;
        dp.Scale = new Vector2(outerRadius);
        dp.InnerScale = new Vector2(innerRadius);
        dp.Radian = MathF.PI * 2f;
        dp.Color = color;
        dp.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Default, DrawTypeEnum.Donut, dp);
    }

    private void DrawFan(ScriptAccessory accessory, string name, uint owner, float length, float radian, int duration, Vector4 color)
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Owner = owner;
        dp.Scale = new Vector2(length);
        dp.Radian = radian;
        dp.Color = color;
        dp.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Default, DrawTypeEnum.Fan, dp);
    }

    private void DrawFanToPartyIndex(ScriptAccessory accessory, string name, uint owner, int targetPartyIndex, float length, float radian, int duration, Vector4 color)
    {
        if (!IsValidPartyIndex(targetPartyIndex)) return;
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Owner = owner;
        dp.TargetObject = accessory.Data.PartyList[targetPartyIndex];
        dp.Scale = new Vector2(length);
        dp.Radian = radian;
        dp.Color = color;
        dp.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Default, DrawTypeEnum.Fan, dp);
    }

    private void DrawFanFromCenter(ScriptAccessory accessory, string name, Vector3 targetPos, float degree, float radius, int duration, Vector4 color)
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Position = Center;
        dp.Rotation = GetRadian(Center, targetPos);
        dp.Radian = degree * MathF.PI / 180f;
        dp.Scale = new Vector2(radius);
        dp.Color = color;
        dp.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Default, DrawTypeEnum.Fan, dp);
    }

    private void DrawFanFromPositionToTarget(ScriptAccessory accessory, string name, Vector3 fromPos, uint targetId, float length, float radian, int duration, Vector4 color)
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Position = fromPos;
        dp.TargetObject = targetId;
        dp.Scale = new Vector2(length);
        dp.Radian = radian;
        dp.Color = color;
        dp.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Default, DrawTypeEnum.Fan, dp);
    }

    private void DrawRect(ScriptAccessory accessory, string name, Vector3 position, Vector2 scale, float rotation, int duration, Vector4 color)
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Position = position;
        dp.Scale = scale;
        dp.Rotation = rotation;
        dp.Color = color;
        dp.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Default, DrawTypeEnum.Rect, dp);
    }

    private void DrawRectToTarget(ScriptAccessory accessory, string name, uint owner, uint target, float width, float length, int duration, Vector4 color)
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Owner = owner;
        dp.TargetObject = target;
        dp.Scale = new Vector2(width, length);
        dp.Color = color;
        dp.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Default, DrawTypeEnum.Rect, dp);
    }

    private void DrawLine(ScriptAccessory accessory, string name, uint source, uint target, int duration, Vector4 color)
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Owner = source;
        dp.TargetObject = target;
        dp.Scale = new Vector2(3f);
        dp.Color = color;
        dp.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Line, dp);
    }

    private void DrawWaypoint(ScriptAccessory accessory, string name, Vector3 position, int duration)
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Position = position;
        dp.Scale = new Vector2(0.5f);
        dp.Color = SafeColor.V4;
        dp.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, dp);
    }

    private void DrawWaypointToObject(ScriptAccessory accessory, string name, uint targetId, int duration, int delay = 0)
    {
        var dp = accessory.Data.GetDefaultDrawProperties();
        dp.Name = name;
        dp.Owner = accessory.Data.Me;
        dp.TargetObject = targetId;
        dp.Scale = new Vector2(2f);
        dp.Color = SafeColor.V4;
        dp.Delay = delay;
        dp.DestoryAt = duration;
        accessory.Method.SendDraw(DrawModeEnum.Imgui, DrawTypeEnum.Displacement, dp);
    }

    #endregion

    #region Position Helpers

    private static bool IsValidPartyIndex(int index) => index is >= 0 and <= 7;

    private static bool IsMtGroup(int index) => index is 0 or 2 or 4 or 6;

    private static bool IsEvenGroup(int index) => index is 0 or 2 or 4 or 6;

    private static bool IsOddGroup(int index) => index is 1 or 3 or 5 or 7;

    private static Vector3 StandardSpreadPosition(int index)
    {
        return index switch
        {
            0 => North,
            1 => South,
            2 => West,
            3 => East,
            4 => new Vector3(90f, 0f, 90f),
            5 => new Vector3(110f, 0f, 90f),
            6 => new Vector3(90f, 0f, 110f),
            7 => new Vector3(110f, 0f, 110f),
            _ => Center
        };
    }

    private static Vector3 PairStackPosition(int index)
    {
        return index switch
        {
            0 or 4 => new Vector3(90f, 0f, 90f),
            1 or 5 => new Vector3(110f, 0f, 90f),
            2 or 6 => new Vector3(90f, 0f, 110f),
            3 or 7 => new Vector3(110f, 0f, 110f),
            _ => Center
        };
    }

    private void TryAssignP2VoidVortex(ScriptAccessory accessory)
    {
        var myIndex = accessory.MyIndex();
        if (!IsValidPartyIndex(myIndex)) return;

        Vector3 targetPos;
        bool isMarked;

        lock (_stateLock)
        {
            if (_p2Assigned || _p2FanTargetIndexes.Count < 4 || _p2TowerCount < 4) return;

            var marked = _p2FanTargetIndexes.Distinct().OrderBy(i => i).ToList();
            var unmarked = Enumerable.Range(0, 8).Where(i => !marked.Contains(i)).OrderBy(i => i).ToList();
            var towers = Enumerable.Range(0, 8).Where(i => _p2TowerExists[i]).OrderBy(i => i).ToList();
            var empties = Enumerable.Range(0, 8).Where(i => !_p2TowerExists[i]).OrderBy(i => i).ToList();

            isMarked = marked.Contains(myIndex);
            var assigned = AssignP2NoCchhSlot(myIndex, marked, unmarked, towers, empties);
            if (assigned < 0) return;

            targetPos = _p2TowerExists[assigned] ? _p2TowerPositions[assigned] : P2Slots[assigned];
            _p2Assigned = true;
        }

        var drawPos = targetPos;
        if (isMarked)
        {
            var dirToCenter = Center - targetPos;
            if (dirToCenter.LengthSquared() > 0.001f)
            {
                drawPos = targetPos + Vector3.Normalize(dirToCenter) * 10f;
            }
        }

        DrawWaypoint(accessory, isMarked ? "MMW_P2_扇形放置点" : "MMW_P2_踩塔点", drawPos, 7000);
        if (!isMarked) DrawCircle(accessory, "MMW_P2_我的塔", drawPos, 6f, 7000, StackColor.V4);
        Text(accessory, isMarked ? "被点名：去本半场空位放扇形" : "未点名：去本半场塔", 7000, true);

        _ = Task.Run(async () =>
        {
            await Task.Delay(10000);
            ResetP2VoidVortex();
        });
    }

    private static int AssignP2NoCchhSlot(int myIndex, List<int> marked, List<int> unmarked, List<int> towers, List<int> empties)
    {
        var isMarked = marked.Contains(myIndex);
        var isUnmarked = unmarked.Contains(myIndex);
        if (!isMarked && !isUnmarked) return -1;

        var isEven = IsEvenGroup(myIndex);
        var playerGroup = (isMarked ? marked : unmarked)
            .Where(i => IsEvenGroup(i) == isEven)
            .OrderBy(i => i)
            .ToList();
        var rank = playerGroup.IndexOf(myIndex);
        if (rank < 0) return -1;

        var candidateSlots = isMarked ? empties : towers;
        var targetSlots = isEven
            ? candidateSlots.Where(i => i is >= 4 and <= 7).OrderByDescending(i => i).ToList()
            : candidateSlots.Where(i => i is >= 0 and <= 3).OrderBy(i => i).ToList();

        if (rank < targetSlots.Count) return targetSlots[rank];

        var fallbackGroup = isMarked ? marked : unmarked;
        rank = fallbackGroup.IndexOf(myIndex);
        return rank >= 0 && rank < candidateSlots.Count ? candidateSlots[rank] : -1;
    }

    private static int GetP2SlotIndex(Vector3 pos)
    {
        var bestIndex = -1;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < P2Slots.Length; i++)
        {
            var dist = Vector3.Distance(pos, P2Slots[i]);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestIndex = i;
            }
        }

        return bestDistance <= 5f ? bestIndex : -1;
    }

    private int FindNearestChaosOrb(Vector3 position)
    {
        var bestIndex = -1;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < _chaosOrbOrder.Count; i++)
        {
            var dist = Vector3.Distance(position, _chaosOrbOrder[i].Position);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private List<int> GetChaosClockwisePriority()
    {
        if (_chaosOrbOrder.Count == 0) return [];

        var center = Center;
        var ordered = _chaosOrbOrder
            .Select((orb, index) => new { index, angle = MathF.Atan2(orb.Position.Z - center.Z, orb.Position.X - center.X) })
            .OrderBy(x => x.angle)
            .Select(x => x.index)
            .ToList();

        return ordered;
    }

    private void ResetChaosCurrent()
    {
        lock (_stateLock)
        {
            _chaosOrbOrder.Clear();
            _chaosYellowLinks.Clear();
            _chaosPurpleLinks.Clear();
            _chaosOrbSpawnCount = 0;
            _chaosYellowAssigned = false;
            _chaosPurpleAssigned = false;
        }
    }

    private void ResetP2VoidVortex()
    {
        lock (_stateLock)
        {
            Array.Clear(_p2TowerExists, 0, _p2TowerExists.Length);
            Array.Clear(_p2TowerPositions, 0, _p2TowerPositions.Length);
            _p2FanTargetIndexes.Clear();
            _p2TowerCount = 0;
            _p2Assigned = false;
            _p2BossId = 0;
        }
    }

    private static Vector3 TrackingFirePosition(int index, int step)
    {
        var angle = (-MathF.PI / 2f) + (MathF.PI / 4f) * ((index + step) % 8);
        return Center + new Vector3(MathF.Cos(angle) * 12f, 0f, MathF.Sin(angle) * 12f);
    }

    private static float GetRadian(Vector3 from, Vector3 to)
    {
        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        return MathF.Atan2(dx, dz);
    }

    #endregion

    #region Utility

    private void Text(ScriptAccessory accessory, string text, int duration, bool isWarning = false)
    {
        if (!EnableText) return;
        accessory.Method.TextInfo(text, duration, isWarning);
    }

    private void Tts(ScriptAccessory accessory, string text, int rate = 0)
    {
        if (!EnableTts) return;
        accessory.Method.TTS(text, rate);
    }

    private void Debug(ScriptAccessory accessory, string message)
    {
        if (!EnableDebug) return;
        accessory.Method.SendChat($"/e [MMW-Enuo-Draft] {message}");
    }

    private static int EventDuration(Event @event, int fallback)
    {
        return int.TryParse(@event["DurationMilliseconds"], out var duration) ? duration : fallback;
    }

    private static Vector3 EventVector(Event @event, string field, Vector3 fallback)
    {
        try
        {
            return JsonConvert.DeserializeObject<Vector3>(@event[field]);
        }
        catch
        {
            return fallback;
        }
    }

    private static float EventFloat(Event @event, string field, float fallback)
    {
        return float.TryParse(@event[field], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static string NormalizeHex(string? raw)
    {
        return (raw ?? string.Empty).Replace("0x", "", StringComparison.OrdinalIgnoreCase).Trim().ToUpperInvariant();
    }

    private static bool ParseObjectId(string? idStr, out uint id)
    {
        id = 0;
        if (string.IsNullOrEmpty(idStr)) return false;

        try
        {
            var clean = idStr.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
            id = uint.Parse(clean, NumberStyles.HexNumber);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private int ParseTargetIconOffset(string id)
    {
        _firstTargetIcon ??= int.Parse(id, NumberStyles.HexNumber);
        return int.Parse(id, NumberStyles.HexNumber) - _firstTargetIcon.Value;
    }

    #endregion
}

public static class MmwEnuoDraftExtensions
{
    public static uint ActionId(this Event evt) => uint.TryParse(evt["ActionId"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;

    public static uint DataId(this Event evt) => uint.TryParse(evt["DataId"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;

    public static uint SourceId(this Event evt) => ParseHexId(evt["SourceId"]);

    public static uint TargetId(this Event evt) => ParseHexId(evt["TargetId"]);

    public static int MyIndex(this ScriptAccessory accessory) => accessory.Data.PartyList.IndexOf(accessory.Data.Me);

    private static uint ParseHexId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        var clean = raw.Replace("0x", "", StringComparison.OrdinalIgnoreCase).Trim();
        return uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ? id : 0;
    }
}
