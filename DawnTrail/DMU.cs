using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Numerics;
using System.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using KodakkuAssist.Module.GameEvent;
using KodakkuAssist.Module.Draw;
using KodakkuAssist.Module.Draw.Manager;
using KodakkuAssist.Module.GameOperate;
using KodakkuAssist.Script;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Dalamud.Utility.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.STD.Helper;
using KodakkuAssist.Data;
using Lumina.Data.Parsing;
using ClientGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace SuzuukiiKodakkuAssist
{

    [ScriptType(name:"妖星乱舞绝境战",
        territorys:[1363],
        guid:"42683c02-0c71-4fd6-b49a-19163b24b22c",
        version:"0.0.0.20",
        note:scriptNotes,
        author:"Suzuukii")]

    public class Dancing_Mad_Ultimate
    {
        
        public const string scriptNotes=
            """
            妖星乱舞绝境战的脚本。
            
            支持进行小队排序测试,可以在聊天框中输入/e kuwutest来检查小队排序是否正确。
            输入/e kuwuclear清除小队排序测试产生的目标标记。

            如果可以的话，请把您的ARR录像发给Discord的可达鸭频道的suzuu_kii，感谢。
            """;
        
        #region User_Settings
        
        [UserSetting("通用 启用文字提示")]
        public bool enablePrompts { get; set; } = false;
        [UserSetting("通用 启用原生TTS")]
        public bool enableVanillaTts { get; set; } = false;
        [UserSetting("通用 启用Daily Routines TTS (需要安装并启用Daily Routines插件!)")]
        public bool enableDailyRoutinesTts { get; set; } = false;
        [UserSetting("通用 机制方向的颜色")]
        public ScriptColor colourOfDirectionIndicators { get; set; } = new() { V4 = new Vector4(1,1,0, 1) }; // Yellow by default.
        [UserSetting("通用 高度危险攻击的颜色")]
        public ScriptColor colourOfExtremelyDangerousAttacks { get; set; } = new() { V4 = new Vector4(1,0,0,1) }; // Red by default.
        [UserSetting("通用 小队排序测试文本发送到的频道")]
        public PartyTestChannels partyTestChannel { get; set; } = PartyTestChannels.默语频道_仅自己可见;
        [UserSetting("调试 启用调试日志并输出到Dalamud日志中")]
        public bool enableDebugLogging { get; set; } = false;
        [UserSetting("调试 忽略所有方法中的阶段检查")]
        public bool skipPhaseChecks { get; set; } = false;
        [UserSetting("调试 在转阶段时保留绘制")]
        public bool preserveDrawingsWhileSwitchingPhase { get; set; } = false;
        [UserSetting("开发者 记录读条ActionId (输出到/e与Dalamud日志, 自动去重)")]
        public bool enableDeveloperActionIdLogging { get; set; } = false;
        [UserSetting("通用 连环环陷阱状态消失前的绘制持续时间(秒)")]
        public double chainTrapMaxDrawDurationSeconds { get; set; } = 5;
        [UserSetting("Beta P1 冰雷技能特效屏蔽 (默认关闭, 使用unsafe写入)")]
        public bool enableBetaPhase1HideIceThunderEffects { get; set; } = false;
        [UserSetting("Beta P1 众神之像3 传送放点指路 (打法绑定, 默认关闭)")]
        public bool enableBetaPhase1GravenImage3TeleportGuide { get; set; } = false;
        [UserSetting("Beta P1 众神之像1 玄乎乎魔法个人指路 (打法绑定, 默认关闭)")]
        public bool enableBetaPhase1GravenImage1ObfuscationGuide { get; set; } = false;
        [UserSetting("Beta P1 众神之像1 连环环陷阱击退指示 (默认关闭)")]
        public bool enableBetaPhase1ChainTrapKnockbackGuide { get; set; } = false;
        #endregion
        
        #region Variables_And_Semaphores
        
        private volatile int majorPhase=1;
        private volatile int phase=1;
        private readonly object developer_loggedActionIdsLock=new object();
        private HashSet<uint> developer_loggedActionIds=new HashSet<uint>();
        
        private volatile int phase1_pulseCannonDrawn=0;
        private readonly object phase1_gravenImage3TeleportStatusGuideLock=new object();
        private List<Phase1GravenImage3TeleportStatusRecord> phase1_gravenImage3TeleportStatusRecords=new List<Phase1GravenImage3TeleportStatusRecord>();
        private volatile bool phase1_gravenImage3TeleportStatusGuideActive=false;
        private volatile int phase1_gravenImage3TeleportStatusGuideGeneration=0;
        private Phase1GravenImage3TeleportStatusStep phase1_gravenImage3TeleportStatusStep1;
        private Phase1GravenImage3TeleportStatusStep phase1_gravenImage3TeleportStatusStep2;
        private volatile bool phase1_isFlagrantFireFake=false;
        private volatile bool phase1_isExpandingFreezeFake=false;
        private volatile bool phase1_isThrummingThunderFake=false;
        private readonly object phase1_flagrantFireTargetsLock=new object();
        private HashSet<ulong> phase1_flagrantFireTargets=new HashSet<ulong>();
        private volatile bool phase1_isFlagrantFireStackIcon=false;
        private ManualResetEvent phase1_flagrantFireTruthConfirmed=new ManualResetEvent(false);
        private ManualResetEvent phase1_flagrantFireIconConfirmed=new ManualResetEvent(false);
        private readonly object phase1_flagrantFireHeadMarkerPairLock=new object();
        private string phase1_pendingFlagrantFirePlayerIcon=string.Empty;
        private string phase1_pendingFlagrantFireObfuscationIcon=string.Empty;
        private int phase1_flagrantFireHeadMarkerPairGeneration=0;
        private volatile int phase1_gravenImage1TowerDrawn=0;
        private readonly object phase1_gravenImage1TowerLock=new object();
        private HashSet<int> phase1_gravenImage1LaserTargetIndexes=new HashSet<int>();
        private List<Vector3> phase1_gravenImage1TowerPositions=new List<Vector3>();
        private readonly object phase1_gravenImage1ObfuscationGuideLock=new object();
        private bool? phase1_gravenImage1IceDiagonalLeftUpRightDown=null;
        private HashSet<ulong> phase1_gravenImage1WaveTetherTargets=new HashSet<ulong>();
        private volatile int phase1_gravenImage1ObfuscationGuideDrawn=0;
        private volatile bool phase1_gravenImage2IsFirstHalf=true;
        
        #endregion
        
        #region Constants_And_Locks
        
        private const int COMMON_INTERVAL=2500;
        private const int MAXIMUM_DURATION=7200000;
        private const int POSITION_MATCH_TOLERANCE=1;
        
        private static readonly Vector3 ARENA_CENTER=new Vector3(100,0,100);
        private const int ARENA_RADIUS=20;
        
        private const int MECHANIC_DATA_WAIT_TIMEOUT=3000;
        private const int PHASE1_HYPERDRIVE_RADIUS=5;
        private const int PHASE1_HYPERDRIVE_DELAY=0;
        private const int PHASE1_HYPERDRIVE_DURATION=7500;
        private const int PHASE1_HYPERDRIVE_SUPPRESS=1000;
        private const int PHASE1_PULSE_WAVE_ARROW_DURATION=5125;
        private static readonly Vector3 PHASE1_PULSE_CANNON_SOURCE_POSITION=new Vector3(100,0,65);
        private const int PHASE1_PULSE_CANNON_DELAY=5625;
        private const int PHASE1_PULSE_CANNON_DURATION=4375;
        private const int PHASE1_SINGLE_TOWER_RADIUS=4;
        private const int PHASE1_SINGLE_TOWER_DRAW_DURATION=3500;
        private const int PHASE1_FLAGRANT_FIRE_DRAW_DURATION=5875;
        private const int PHASE1_EXPANDING_FREEZE_DURATION=5000;
        private const int PHASE1_THRUMMING_THUNDER_DURATION=5000;
        private const int PHASE1_ACTION_EFFECT_HIDE_DURATION=5000;
        private const int PHASE1_ACTION_EFFECT_HIDE_RECOVERY_DELAY=125;
        private const int PHASE1_ACTION_EFFECT_HIDE_TOTAL_DURATION=PHASE1_ACTION_EFFECT_HIDE_DURATION+PHASE1_ACTION_EFFECT_HIDE_RECOVERY_DELAY;
        private const int PHASE1_EXPLOSION_DRAW_DURATION=3000;
        private const int PHASE1_CHAIN_TRAP_RADIUS=6;
        private const int PHASE1_CHAIN_TRAP_KNOCKBACK_GUIDE_DURATION=5000;
        private const int PHASE1_GRAVEN_IMAGE3_TELEPORT_STATUS_GUIDE_DURATION=15000;
        private const float PHASE1_GRAVEN_IMAGE3_TELEPORT_STATUS_GUIDE_RADIUS=1.2f;
        private const string PHASE1_GRAVEN_IMAGE3_TELEPORT_STATUS_GUIDE_DRAW_PREFIX="Beta_P1_众神之像3_传送放点指路";
        private const int PHASE1_GRAVEN_IMAGE1_OBFUSCATION_FIRST_GUIDE_DURATION=3000;
        private const int PHASE1_GRAVEN_IMAGE1_OBFUSCATION_DATA_WAIT_INTERVAL=100;
        private const int PHASE1_GRAVEN_IMAGE1_OBFUSCATION_DATA_WAIT_RETRIES=5;
        private const int PHASE1_GRAVEN_IMAGE1_OBFUSCATION_SECOND_GUIDE_DELAY=5000;
        private const int PHASE1_GRAVEN_IMAGE1_OBFUSCATION_SECOND_GUIDE_DURATION=5000;
        private const int PHASE1_GRAVEN_IMAGE2_FIRST_GRAVITY_DELAY=0;
        private const int PHASE1_GRAVEN_IMAGE2_FIRST_GRAVITY_DURATION=6500;
        private const int PHASE1_GRAVEN_IMAGE2_SECOND_GRAVITY_DELAY=3875;
        private const int PHASE1_GRAVEN_IMAGE2_SECOND_GRAVITY_DURATION=4625;
        private const int PHASE1_GRAVEN_IMAGE2_FIRST_STONE_DELAY=6500;
        private const int PHASE1_GRAVEN_IMAGE2_SECOND_STONE_DELAY=8500;
        private const int PHASE1_GRAVEN_IMAGE2_STONE_DURATION=4000;
        private const int PHASE1_GRAVEN_IMAGE2_PROJECTILE_RADIUS=5;
        private const int PHASE1_HALF_ARENA_DRAW_DURATION=5000;
        private const int PHASE1_STATUE_GAZE_PROMPT_DURATION=3000;
        private const string PHASE2_OPENING_TIMELINE_SOURCE_DATA_ID="19506";
        private const int PHASE2_TERMINAL_DUAL_ARMS_TARGET_CIRCLE_RADIUS=5;
        private const int PHASE2_TERMINAL_DUAL_ARMS_TARGET_CIRCLE_DURATION=5000;
        private const int DEVELOPER_MAX_LOGGED_ACTION_IDS=256;
        private const int PARTY_TEST_DURATION=20000;
        
        private static readonly int[] PHASE1_GRAVEN_IMAGE1_TOWER_PRIORITY=new int[] {3,2,1,0,4,5,6,7};
        private static readonly Vector3 PHASE1_GRAVEN_IMAGE1_UPPER_LEFT_POINT=new Vector3(93.90f,0,93.94f);
        private static readonly Vector3 PHASE1_GRAVEN_IMAGE1_UPPER_RIGHT_POINT=new Vector3(106.09f,0,93.93f);
        private static readonly Vector3[] PHASE1_GRAVEN_IMAGE1_RIGHT_SIDE_GUIDE_POINTS=new Vector3[] {
            new Vector3(103,0,100),
            new Vector3(109,0,100),
            new Vector3(113,0,100),
            new Vector3(119,0,100)
        };
        private static readonly Vector3[] PHASE1_GRAVEN_IMAGE1_LEFT_SIDE_GUIDE_POINTS=new Vector3[] {
            new Vector3(97,0,100),
            new Vector3(91,0,100),
            new Vector3(87,0,100),
            new Vector3(81,0,100)
        };
        private static readonly Vector3 PHASE1_GRAVEN_IMAGE2_GRAVITY_SOURCE_POSITION=new Vector3(102.5f,22.5f,27);
        private static readonly Vector3 PHASE1_GRAVEN_IMAGE2_STONE_SOURCE_POSITION=new Vector3(126,7,41.5f);
        private static readonly Vector3 PHASE1_GRAVEN_IMAGE2_LEFT_SLASH_SOURCE_POSITION=new Vector3(92,15,27);
        private static readonly Vector3 PHASE1_GRAVEN_IMAGE2_RIGHT_SLASH_SOURCE_POSITION=new Vector3(116,6.5f,43);
        private static readonly Vector3 PHASE1_GRAVEN_IMAGE3_LOOK_AWAY_SOURCE_POSITION=new Vector3(105.25f,13.5f,34);
        private static readonly Vector3 PHASE1_GRAVEN_IMAGE3_LOOK_AT_SOURCE_POSITION=new Vector3(95,12.5f,25);
        
        #endregion
        
        #region Enumerations_And_Classes
        
        public enum PartyTestChannels {
            
            不发送到任何频道,
            默语频道_仅自己可见,
            小队频道_所有队员可见

        }
        
        private struct Phase1GravenImage3TeleportStatusRecord {
            
            public uint StatusId;
            public long AddedAtMilliseconds;
            public int DurationMilliseconds;
            
        }
        
        private struct Phase1GravenImage3TeleportStatusStep {
            
            public uint StatusId;
            public Vector3 Position;
            
        }
        
        #endregion
        
        #region Initialization
        
        public void Init(ScriptAccessory accessory) {
            
            accessory.Method.RemoveDraw(".*");
            
            variableAndSemaphoreInitialization();
            
        }

        private void variableAndSemaphoreInitialization() {

            majorPhase=1;
            phase=1;
            
            lock(developer_loggedActionIdsLock) {
                
                developer_loggedActionIds.Clear();
                
            }
            resetPhase1TransientData();
            resetPhase1GravenImage3TeleportData();

        }
        
        #endregion
        
        #region Global
        
        [ScriptMethod(name:"通用 小队排序测试",
            eventType:EventTypeEnum.Chat,
            eventCondition:["Type:Echo"])]

        public void 通用_小队排序测试(Event @event,ScriptAccessory accessory) {

            string processedText=(@event["Message"]).Trim().ToLower();
            
            if(!string.Equals(processedText,"kuwutest")) {

                return;

            }
            
            string text="请确认如下小队排序是否正确:\n";
            string log=string.Empty;
            KodakkuAssist.Data.IGameObject? sourceObject=null;
            string[] roles=["MT",
                            "ST",
                            "H1",
                            "H2",
                            "D1",
                            "D2",
                            "D3",
                            "D4"];
            KodakkuAssist.Module.GameOperate.MarkType[] marks=[MarkType.Stop1, // MT
                                                               MarkType.Stop2, // OT (ST)
                                                               MarkType.Bind1, // H1
                                                               MarkType.Bind2, // H2
                                                               MarkType.Attack1, // M1 (D1)
                                                               MarkType.Attack2, // M2 (D2)
                                                               MarkType.Attack3, // R1 (D3)
                                                               MarkType.Attack4]; // R2 (D4)

            for(int i=0;i<marks.Length;++i) {
                
                accessory.Method.Mark(accessory.Data.PartyList[i],marks[i]);
                
                sourceObject=accessory.Data.Objects.SearchById(accessory.Data.PartyList[i]);
                
                if(sourceObject==null) {

                    continue;
                
                }
                
                else {
                
                    if(sourceObject is not ICharacter sourceICharacter) {

                        continue;
                    
                    }

                    else {
                        
                        text+=$"{roles[i]}:{sourceObject.Name}，标记{marks[i].ToString()}。";

                        if(i<marks.Length-1) {

                            text+="\n";

                        }
                        
                        log+=$"Mark {accessory.Data.PartyList[i]} as {marks[i].ToString()}\n";

                    }
                
                }
                
            }

            switch(partyTestChannel) {

                case PartyTestChannels.不发送到任何频道: {

                    break;

                }
                
                case PartyTestChannels.默语频道_仅自己可见: {
                    
                    accessory.Method.SendChat($"/e \n{text}");

                    break;

                }
                
                case PartyTestChannels.小队频道_所有队员可见: {
                    
                    accessory.Method.SendChat($"/p \n{text}");

                    break;

                }
                
                default: {

                    break;

                }
                
            }

            if(enablePrompts) {

                accessory.Method.TextInfo(text,PARTY_TEST_DURATION);
                
            }
            
            accessory.tts(text,enableVanillaTts,enableDailyRoutinesTts);

            if(enableDebugLogging) {
                
                accessory.Log.Debug($"\n----- Party Test Text -----\n{text}\n\n----- Party Test Log -----\n{log}");
                
            }

        }
        
        [ScriptMethod(name:"通用 小队排序测试清除",
            eventType:EventTypeEnum.Chat,
            eventCondition:["Type:Echo"],
            userControl:false)]

        public void 通用_小队排序测试清除(Event @event,ScriptAccessory accessory) {

            string processedText=(@event["Message"]).Trim().ToLower();
            
            if(!string.Equals(processedText,"kuwuclear")) {

                return;

            }
            
            accessory.Method.MarkClear();
            
            if(enableDebugLogging) {
                
                accessory.Log.Debug("Now trying to clear party test signs...");
                
            }

        }
        
        [ScriptMethod(name:"开发者 记录读条ActionId",
            eventType:EventTypeEnum.StartCasting,
            eventCondition:["ActionId:regex:^\\d+$"],
            userControl:false)]

        public void 开发者_记录读条ActionId(Event @event,ScriptAccessory accessory) {
            
            if(!enableDeveloperActionIdLogging) {
                
                return;
                
            }
            
            if(!@event.TryActionId(out uint actionId)) {
                
                return;
                
            }
            
            lock(developer_loggedActionIdsLock) {
                
                if(developer_loggedActionIds.Count>=DEVELOPER_MAX_LOGGED_ACTION_IDS) {
                    
                    developer_loggedActionIds.Clear();
                    
                }
                
                if(!developer_loggedActionIds.Add(actionId)) {
                    
                    return;
                    
                }
                
            }
            
            string sourceIdText=@event.TrySourceId(out ulong sourceId)?$"0x{sourceId:X}":"-";
            string sourceDataIdText=string.IsNullOrWhiteSpace(@event["SourceDataId"])?"-":@event["SourceDataId"];
            string durationText=string.IsNullOrWhiteSpace(@event["DurationMilliseconds"])?"-":@event["DurationMilliseconds"];
            string positionText=@event.TrySourcePosition(out Vector3 sourcePosition)?$"({sourcePosition.X:F2},{sourcePosition.Y:F2},{sourcePosition.Z:F2})":"-";
            string rotationText=@event.TrySourceRotation(out float sourceRotation)?sourceRotation.ToString("F3",System.Globalization.CultureInfo.InvariantCulture):"-";
            
            string message=$"ActionId={actionId}, SourceId={sourceIdText}, SourceDataId={sourceDataIdText}, DurationMs={durationText}, Pos={positionText}, Rot={rotationText}";
            
            accessory.Log.Debug($"[DMU Developer] {message}");
            accessory.Method.SendChat($"/e [DMU Developer] {message}");

        }
        
        #endregion
        
        #region Major_Phase_1
        
        [ScriptMethod(name:"P1 恶狠狠毁荡 (范围)",
            eventType:EventTypeEnum.StartCasting,
            eventCondition:["ActionId:50179"])]

        public void P1_恶狠狠毁荡_范围(Event @event,ScriptAccessory accessory) {
            
            if(majorPhase!=1&&!skipPhaseChecks) {

                return;

            }
            
            if(!convertObjectIdToDecimal(@event["SourceId"],out var sourceId)) {
                
                return;
                
            }
            
            if(!convertObjectIdToDecimal(@event["TargetId"],out var targetId)) {
                
                return;
                
            }
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();

            currentProperties.Scale=new(100);
            currentProperties.Radian=float.Pi/3*2;
            currentProperties.Owner=sourceId;
            currentProperties.TargetObject=targetId;
            currentProperties.Color=colourOfExtremelyDangerousAttacks.V4.WithW(1);
            currentProperties.DestoryAt=5000;
        
            accessory.Method.SendDraw(DrawModeEnum.Default,DrawTypeEnum.Fan,currentProperties);
            
            currentProperties=accessory.Data.GetDefaultDrawProperties();

            currentProperties.Scale=new(100);
            currentProperties.Radian=float.Pi/3*2;
            currentProperties.Owner=sourceId;
            currentProperties.TargetResolvePattern=PositionResolvePatternEnum.OwnerEnmityOrder;
            currentProperties.TargetOrderIndex=2;
            currentProperties.Color=colourOfExtremelyDangerousAttacks.V4.WithW(1);
            currentProperties.Delay=5000;
            currentProperties.DestoryAt=3375;
        
            accessory.Method.SendDraw(DrawModeEnum.Default,DrawTypeEnum.Fan,currentProperties);

        }
        
        [ScriptMethod(name:"P1 超驱动 (范围)",
            eventType:EventTypeEnum.ActionEffect,
            eventCondition:["ActionId:50722"],
            suppress:PHASE1_HYPERDRIVE_SUPPRESS)]

        public void P1_超驱动_范围(Event @event,ScriptAccessory accessory) {
            
            if(!isInMajorPhase(1)) {
                
                return;
                
            }
            
            if(!@event.TrySourceId(out var sourceId)) {
                
                return;
                
            }
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();
            
            currentProperties.Name=$"P1_超驱动_范围_{sourceId:X}";
            currentProperties.Scale=new(PHASE1_HYPERDRIVE_RADIUS);
            currentProperties.Owner=sourceId;
            currentProperties.CentreResolvePattern=PositionResolvePatternEnum.OwnerEnmityOrder;
            currentProperties.CentreOrderIndex=1;
            currentProperties.Color=colourOfExtremelyDangerousAttacks.V4.WithW(1);
            currentProperties.Delay=PHASE1_HYPERDRIVE_DELAY;
            currentProperties.DestoryAt=PHASE1_HYPERDRIVE_DURATION;
            
            accessory.Method.SendDraw(DrawModeEnum.Default,DrawTypeEnum.Circle,currentProperties);
            
        }
        
        [ScriptMethod(name:"P1 众神之像 (阶段控制)",
            eventType:EventTypeEnum.StartCasting,
            eventCondition:["ActionId:48370"],
            userControl:false)]

        public void P1_众神之像_阶段控制(Event @event,ScriptAccessory accessory) {
            
            if(!isInMajorPhase(1)) {

                return;

            }
            
            resetPhase1TransientData();
            
            int currentPhase=Interlocked.Increment(ref phase);
            
            debugLog(accessory,$"P1 众神之像: majorPhase={majorPhase}, phase={currentPhase}。");

        }
        
        [ScriptMethod(name:"P1 众神之像1 波动弹 (击退指示)",
            eventType:EventTypeEnum.Tether,
            eventCondition:["Id:002D"])]

        public void P1_众神之像1_波动弹_击退指示(Event @event,ScriptAccessory accessory) {
            
            if(!isInMajorPhase(1)) {

                return;

            }
            
            if(!@event.TryTargetId(out var targetId)) {
                
                return;
                
            }
            
            if(enableBetaPhase1GravenImage1ObfuscationGuide&&isInPhase1SubPhase(2)) {
                
                lock(phase1_gravenImage1ObfuscationGuideLock) {
                    
                    phase1_gravenImage1WaveTetherTargets.Add(targetId);
                    
                }
                
            }
            
            if(targetId!=accessory.Data.Me) {
                
                return;
                
            }
            
            drawFixedArrowOnMe(accessory,new Vector2(2,13),0,colourOfDirectionIndicators.V4.WithW(1),PHASE1_PULSE_WAVE_ARROW_DURATION,"P1_众神之像1_波动弹_击退指示");

        }
        
        [ScriptMethod(name:"P1 玄乎乎魔法 (数据收集)",
            eventType:EventTypeEnum.TargetIcon,
            eventCondition:["Id:regex:^(02A1|02A2|02A3|02A4|02A5|02A6)$"],
            userControl:false)]

        public void P1_玄乎乎魔法_数据收集(Event @event,ScriptAccessory accessory) {
            
            if(!isInMajorPhase(1)) {

                return;

            }
            
            if(!convertObjectIdToDecimal(@event["TargetId"],out var targetId)) {
                
                return;
                
            }
            
            var targetObject=accessory.Data.Objects.SearchById(targetId);

            if(targetObject==null||!targetObject.IsValid()||targetObject.DataId!=19504) {

                return;

            }
            
            collectPhase1ObfuscationData(@event,accessory);

        }
        
        [ScriptMethod(name:"P1 众神之像1 玄乎乎魔法冰方向 (Beta指路数据)",
            eventType:EventTypeEnum.StartCasting,
            eventCondition:["ActionId:regex:^(47768|47771)$"],
            userControl:false)]

        public void P1_众神之像1_玄乎乎魔法冰方向_Beta指路数据(Event @event,ScriptAccessory accessory) {
            
            if(!enableBetaPhase1GravenImage1ObfuscationGuide) {
                
                return;
                
            }
            
            if(!isInPhase1SubPhase(2)) {

                return;

            }
            
            if(!@event.TryActionId(out var actionId)) {
                
                return;
                
            }
            
            if(!@event.TrySourceRotation(out var sourceRotation)) {
                
                debugLog(accessory,"P1 众神之像1 玄乎乎魔法个人指路: SourceRotation解析失败。");
                return;
                
            }
            
            bool isRealIce=actionId==47768;
            bool rawDiagonalLeftUpRightDown=isPhase1GravenImage1IceDiagonalLeftUpRightDown(sourceRotation);
            bool actualDiagonalLeftUpRightDown=isRealIce?rawDiagonalLeftUpRightDown:!rawDiagonalLeftUpRightDown;
            
            lock(phase1_gravenImage1ObfuscationGuideLock) {
                
                phase1_gravenImage1IceDiagonalLeftUpRightDown=actualDiagonalLeftUpRightDown;
                
            }
            
            debugLog(accessory,$"P1 众神之像1 玄乎乎魔法个人指路: iceAction={actionId}, rotation={sourceRotation}, actualDiagonal={(actualDiagonalLeftUpRightDown?"左上右下":"左下右上")}。");
            
        }
        
        [ScriptMethod(name:"P1 众神之像1 玄乎乎魔法个人指路 (Beta)",
            eventType:EventTypeEnum.StartCasting,
            eventCondition:["ActionId:47764"])]

        public void P1_众神之像1_玄乎乎魔法个人指路_Beta(Event @event,ScriptAccessory accessory) {
            
            if(!enableBetaPhase1GravenImage1ObfuscationGuide) {
                
                return;
                
            }
            
            if(!isInPhase1SubPhase(2)) {

                return;

            }
            
            if(Interlocked.Exchange(ref phase1_gravenImage1ObfuscationGuideDrawn,1)==1) {
                
                return;
                
            }
            
            _=resolvePhase1GravenImage1ObfuscationGuideAfterDelay(accessory);
            
        }
        
        [ScriptMethod(name:"P1 呼啦啦爆炎 (数据收集)",
            eventType:EventTypeEnum.TargetIcon,
            eventCondition:["Id:regex:^(0080|007F)$"],
            userControl:false)]

        public void P1_呼啦啦爆炎_数据收集(Event @event,ScriptAccessory accessory) {
            
            if(!isInMajorPhase(1)) {

                return;

            }
            
            collectPhase1FlagrantFireIcon(@event,accessory);

        }
        
        [ScriptMethod(name:"P1 呼啦啦爆炎 (范围与提示)",
            eventType:EventTypeEnum.TargetIcon,
            eventCondition:["Id:regex:^(0080|007F)$"],
            suppress:COMMON_INTERVAL)]

        public void P1_呼啦啦爆炎_范围与提示(Event @event,ScriptAccessory accessory) {
            
            if(!isInMajorPhase(1)) {

                return;

            }
            
            collectPhase1FlagrantFireIcon(@event,accessory);
            
            bool hasTruth=phase1_flagrantFireTruthConfirmed.WaitOne(MECHANIC_DATA_WAIT_TIMEOUT);
            bool hasIcon=phase1_flagrantFireIconConfirmed.WaitOne(MECHANIC_DATA_WAIT_TIMEOUT);
            
            if(!hasTruth||!hasIcon) {
                
                debugLog(accessory,$"P1 呼啦啦爆炎: 等待数据超时, hasTruth={hasTruth}, hasIcon={hasIcon}。");
                return;
                
            }
            
            bool isFake=phase1_isFlagrantFireFake;
            bool isStackIcon=phase1_isFlagrantFireStackIcon;
            List<ulong> targets;
            
            lock(phase1_flagrantFireTargetsLock) {
                
                targets=phase1_flagrantFireTargets.ToList();
                
            }

            if(isStackIcon&&!isFake) {
                
                foreach(ulong target in targets) {
                    
                    drawCircleOnObject(accessory,target,6,colourOfDirectionIndicators.V4.WithW(1),PHASE1_FLAGRANT_FIRE_DRAW_DURATION,$"P1_呼啦啦爆炎_真分摊_{target:X}");
                    
                }
                
                return;
                
            }
            
            if(isStackIcon&&isFake) {
                
                foreach(var partyMember in accessory.Data.PartyList) {
                    
                    drawCircleOnObject(accessory,partyMember,5,accessory.Data.DefaultDangerColor,PHASE1_FLAGRANT_FIRE_DRAW_DURATION,$"P1_呼啦啦爆炎_假分摊_{partyMember:X}");
                    
                }
                
                return;
                
            }
            
            if(!isStackIcon&&!isFake) {
                
                foreach(ulong target in targets) {
                    
                    drawCircleOnObject(accessory,target,5,accessory.Data.DefaultDangerColor,PHASE1_FLAGRANT_FIRE_DRAW_DURATION,$"P1_呼啦啦爆炎_真分散_{target:X}");
                    
                }
                
                return;
                
            }
            
            string prompt="职能四四分摊";
            
            if(enablePrompts) {
                
                accessory.Method.TextInfo(prompt,PHASE1_FLAGRANT_FIRE_DRAW_DURATION);
                
            }
            
            accessory.tts(prompt,enableVanillaTts,enableDailyRoutinesTts);

        }
        
        [ScriptMethod(name:"P1 呼啦啦爆炎 真假头标配对提示",
            eventType:EventTypeEnum.TargetIcon,
            eventCondition:["Id:regex:^(0080|007F|02A1|02A2)$"],
            userControl:false)]

        public void P1_呼啦啦爆炎_真假头标配对提示(Event @event,ScriptAccessory accessory) {
            
            if(!isInMajorPhase(1)) {

                return;

            }
            
            if(!tryNormalizeIconId(@event["Id"],out string iconId)) {
                
                return;
                
            }
            
            bool isPlayerIcon=iconId=="0080"||iconId=="007F";
            bool isObfuscationIcon=iconId=="02A1"||iconId=="02A2";
            
            if(isObfuscationIcon) {
                
                if(!@event.TryTargetId(out var targetId)) {
                    
                    return;
                    
                }
                
                var targetObject=accessory.Data.Objects.SearchById(targetId);
                
                if(targetObject==null||!targetObject.IsValid()||targetObject.DataId!=19504) {
                    
                    return;
                    
                }
                
            }
            
            if(!isPlayerIcon&&!isObfuscationIcon) {
                
                return;
                
            }
            
            capturePhase1FlagrantFireHeadMarkerPair(accessory,iconId,isPlayerIcon);
            
        }
        
        [ScriptMethod(name:"P1 呼啦啦爆炎 (数据清除)",
            eventType:EventTypeEnum.ActionEffect,
            eventCondition:["ActionId:regex:^(47778|47779)$"],
            suppress:COMMON_INTERVAL,
            userControl:false)]

        public void P1_呼啦啦爆炎_数据清除(Event @event,ScriptAccessory accessory) {
            
            if(!isInMajorPhase(1)) {

                return;

            }
            
            resetPhase1FlagrantFireData();

        }
        
        [ScriptMethod(name:"P1 扩大大冰封 (实际范围)",
            eventType:EventTypeEnum.StartCasting,
            eventCondition:["ActionId:regex:^(47768|47774)$"])]

        public void P1_扩大大冰封_实际范围(Event @event,ScriptAccessory accessory) {
            
            if(!isInMajorPhase(1)) {

                return;

            }
            
            if(!convertObjectIdToDecimal(@event["SourceId"],out var sourceId)) {
                
                return;
                
            }
            
            drawFanOnObject(accessory,
                sourceId,
                40,
                float.Pi/2,
                colourOfExtremelyDangerousAttacks.V4.WithW(1),
                PHASE1_EXPANDING_FREEZE_DURATION,
                $"P1_扩大大冰封_范围_{sourceId:X}");

        }
        
        [ScriptMethod(name:"P1 众神之像1 波动炮 (范围)",
            eventType:EventTypeEnum.StartCasting,
            eventCondition:["ActionId:47764"])]

        public void P1_众神之像1_波动炮_范围(Event @event,ScriptAccessory accessory) {
            
            if(!isInMajorPhase(1)) {

                return;

            }
            
            if(Interlocked.Exchange(ref phase1_pulseCannonDrawn,1)==1) {
                
                return;
                
            }
            
            foreach(var partyMember in accessory.Data.PartyList) {
                
                drawDelayedRectToObject(accessory,
                    PHASE1_PULSE_CANNON_SOURCE_POSITION,
                    partyMember,
                    new Vector2(6,100),
                    accessory.Data.DefaultDangerColor,
                    PHASE1_PULSE_CANNON_DELAY,
                    PHASE1_PULSE_CANNON_DURATION,
                    $"P1_众神之像1_波动炮_范围_{partyMember:X}");
                
            }

        }
        
        [ScriptMethod(name:"P1 众神之像1 爆炸 (精确范围)",
            eventType:EventTypeEnum.StartCasting,
            eventCondition:["ActionId:47786"])]

        public void P1_众神之像1_爆炸_精确范围(Event @event,ScriptAccessory accessory) {
            
            if(!isInPhase1SubPhase(2)) {

                return;

            }
            
            if(!@event.TryEffectPosition(out var effectPosition)) {
                
                debugLog(accessory,"P1 众神之像1 爆炸: EffectPosition解析失败。");
                return;
                
            }
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();
            
            currentProperties.Name=$"P1_众神之像1_爆炸_精确范围_{effectPosition.X:F1}_{effectPosition.Z:F1}";
            currentProperties.Scale=new(4);
            currentProperties.Position=effectPosition;
            currentProperties.Color=colourOfDirectionIndicators.V4.WithW(1);
            currentProperties.DestoryAt=PHASE1_EXPLOSION_DRAW_DURATION;
            
            accessory.Method.SendDraw(DrawModeEnum.Imgui,DrawTypeEnum.Circle,currentProperties);
            
        }
        
        [ScriptMethod(name:"P1 众神之像1 连环环陷阱 (范围)",
            eventType:EventTypeEnum.StatusAdd,
            eventCondition:["StatusID:5078"])]

        public void P1_众神之像1_连环环陷阱_范围(Event @event,ScriptAccessory accessory) {
            
            if(!isInPhase1SubPhase(2)) {

                return;

            }
            
            if(!convertObjectIdToDecimal(@event["TargetId"],out var targetId)) {
                
                return;
                
            }
            
            if(!tryGetEventDurationMilliseconds(@event,accessory,out int durationMilliseconds)) {
                
                return;
                
            }
            
            getLastWindowDrawTiming(durationMilliseconds,chainTrapMaxDrawDurationSeconds,out int drawDelay,out int drawDuration);
            
            drawCircleOnObject(accessory,
                targetId,
                PHASE1_CHAIN_TRAP_RADIUS,
                accessory.Data.DefaultDangerColor,
                drawDuration,
                getPhase1ChainTrapDrawName(targetId),
                drawDelay,
                DrawModeEnum.Imgui);
            
        }
        
        [ScriptMethod(name:"P1 众神之像1 连环环陷阱击退指示 (Beta)",
            eventType:EventTypeEnum.StatusAdd,
            eventCondition:["StatusID:5078"])]

        public void P1_众神之像1_连环环陷阱击退指示_Beta(Event @event,ScriptAccessory accessory) {
            
            if(!enableBetaPhase1ChainTrapKnockbackGuide) {
                
                return;
                
            }
            
            if(!isInPhase1SubPhase(2)) {

                return;

            }
            
            if(!@event.TryTargetId(out var targetId)) {
                
                return;
                
            }
            
            if(!tryGetEventDurationMilliseconds(@event,accessory,out int durationMilliseconds)) {
                
                return;
                
            }
            
            getLastWindowDrawTiming(durationMilliseconds,chainTrapMaxDrawDurationSeconds,out int drawDelay,out int drawDuration);
            
            int guideDuration=Math.Min(drawDuration,PHASE1_CHAIN_TRAP_KNOCKBACK_GUIDE_DURATION);
            
            drawKnockbackGuideFromTrap(accessory,
                targetId,
                guideDuration,
                getPhase1ChainTrapKnockbackGuideDrawName(targetId),
                drawDelay);
            
        }
        
        [ScriptMethod(name:"P1 众神之像1 连环环陷阱 (范围清除)",
            eventType:EventTypeEnum.StatusRemove,
            eventCondition:["StatusID:5078"],
            userControl:false)]

        public void P1_众神之像1_连环环陷阱_范围清除(Event @event,ScriptAccessory accessory) {
            
            if(!convertObjectIdToDecimal(@event["TargetId"],out var targetId)) {
                
                return;
                
            }
            
            accessory.Method.RemoveDraw(getPhase1ChainTrapDrawName(targetId));
            
        }
        
        [ScriptMethod(name:"P1 众神之像1 单人塔 (指路)",
            eventType:EventTypeEnum.ActionEffect,
            eventCondition:["ActionId:47784","TargetIndex:1"])]

        public void P1_众神之像1_单人塔_指路(Event @event,ScriptAccessory accessory) {
            
            if(!isInPhase1SubPhase(2)) {

                return;

            }
            
            if(!@event.TryTargetId(out var targetId)) {
                
                return;
                
            }
            
            int targetIndex=accessory.getPlayerIndex(targetId);
            
            if(targetIndex<0) {
                
                return;
                
            }
            
            if(!@event.TryTargetPosition(out var towerPosition)) {
                
                debugLog(accessory,"P1 众神之像1 单人塔: TargetPosition解析失败。");
                return;
                
            }
            
            List<int> laserTargetIndexes;
            List<Vector3> towerPositions;
            bool shouldResolve=false;
            
            lock(phase1_gravenImage1TowerLock) {
                
                if(!phase1_gravenImage1LaserTargetIndexes.Add(targetIndex)) {
                    
                    return;
                    
                }
                
                phase1_gravenImage1TowerPositions.Add(towerPosition);
                
                if(phase1_gravenImage1TowerPositions.Count==4) {
                    
                    shouldResolve=true;
                    laserTargetIndexes=phase1_gravenImage1LaserTargetIndexes.ToList();
                    towerPositions=phase1_gravenImage1TowerPositions.ToList();
                    
                } else {
                    
                    laserTargetIndexes=new List<int>();
                    towerPositions=new List<Vector3>();
                    
                }
                
            }
            
            if(!shouldResolve) {
                
                return;
                
            }
            
            if(Interlocked.Exchange(ref phase1_gravenImage1TowerDrawn,1)==1) {
                
                return;
                
            }
            
            resolvePhase1GravenImage1Tower(accessory,laserTargetIndexes,towerPositions);
            
        }
        
        [ScriptMethod(name:"P1 劈啪啪暴雷 (实际范围)",
            eventType:EventTypeEnum.StartCasting,
            eventCondition:["ActionId:regex:^(47775|47777)$"])]

        public void P1_劈啪啪暴雷_实际范围(Event @event,ScriptAccessory accessory) {
            
            if(!isInMajorPhase(1)) {

                return;

            }
            
            if(!convertObjectIdToDecimal(@event["SourceId"],out var sourceId)) {
                
                return;
                
            }
            
            drawRectOnObject(accessory,
                sourceId,
                new Vector2(10,40),
                colourOfExtremelyDangerousAttacks.V4.WithW(1),
                PHASE1_THRUMMING_THUNDER_DURATION,
                $"P1_劈啪啪暴雷_范围_{sourceId:X}",
                DrawModeEnum.Imgui);

        }
        
        [ScriptMethod(name:"P1 冰雷技能特效屏蔽 (Beta)",
            eventType:EventTypeEnum.StartCasting,
            eventCondition:["ActionId:regex:^(47768|47771|47775|47776)$"],
            userControl:false)]

        public void P1_冰雷技能特效屏蔽_Beta(Event @event,ScriptAccessory accessory) {
            
            if(!enableBetaPhase1HideIceThunderEffects) {
                
                return;
                
            }
            
            if(!isInMajorPhase(1)) {

                return;

            }
            
            if(!@event.TrySourceId(out var sourceId)) {
                
                return;
                
            }
            
            if(!accessory.TrySetObjectVisible(sourceId,false,out string message,PHASE1_ACTION_EFFECT_HIDE_TOTAL_DURATION)) {
                
                debugLog(accessory,$"P1 冰雷技能特效屏蔽: {message}");
                
            }

        }
        
        [ScriptMethod(name:"P1 众神之像2 重力弹与岩石弹 (范围)",
            eventType:EventTypeEnum.Tether,
            eventCondition:["Id:002D"])]

        public void P1_众神之像2_重力弹与岩石弹_范围(Event @event,ScriptAccessory accessory) {
            
            if(!isInPhase1SubPhase(3)) {

                return;

            }
            
            if(!@event.TryTargetId(out var targetId)) {
                
                return;
                
            }
            
            if(!@event.TrySourcePosition(out var sourcePosition)) {
                
                debugLog(accessory,"P1 众神之像2 重力弹与岩石弹: SourcePosition解析失败。");
                return;
                
            }
            
            int delay=-1;
            int duration=-1;
            Vector4 colour=accessory.Data.DefaultDangerColor;
            string mechanicName=string.Empty;
            
            if(isNearPosition(sourcePosition,PHASE1_GRAVEN_IMAGE2_GRAVITY_SOURCE_POSITION)) {
                
                mechanicName="重力弹";
                colour=colourOfDirectionIndicators.V4.WithW(1);
                
                if(phase1_gravenImage2IsFirstHalf) {
                    
                    delay=PHASE1_GRAVEN_IMAGE2_FIRST_GRAVITY_DELAY;
                    duration=PHASE1_GRAVEN_IMAGE2_FIRST_GRAVITY_DURATION;
                    
                } else {
                    
                    delay=PHASE1_GRAVEN_IMAGE2_SECOND_GRAVITY_DELAY;
                    duration=PHASE1_GRAVEN_IMAGE2_SECOND_GRAVITY_DURATION;
                    
                }
                
            }
            
            if(isNearPosition(sourcePosition,PHASE1_GRAVEN_IMAGE2_STONE_SOURCE_POSITION)) {
                
                mechanicName="岩石弹";
                colour=accessory.Data.DefaultDangerColor;
                delay=phase1_gravenImage2IsFirstHalf?PHASE1_GRAVEN_IMAGE2_FIRST_STONE_DELAY:PHASE1_GRAVEN_IMAGE2_SECOND_STONE_DELAY;
                duration=PHASE1_GRAVEN_IMAGE2_STONE_DURATION;
                
            }
            
            if(delay<0||duration<0) {
                
                debugLog(accessory,$"P1 众神之像2 重力弹与岩石弹: 未匹配SourcePosition={sourcePosition}。");
                return;
                
            }
            
            drawCircleOnObject(accessory,
                targetId,
                PHASE1_GRAVEN_IMAGE2_PROJECTILE_RADIUS,
                colour,
                duration,
                $"P1_众神之像2_{mechanicName}_范围_{targetId:X}",
                delay,
                DrawModeEnum.Imgui);
            
        }
        
        [ScriptMethod(name:"P1 众神之像2 恶狠狠毁荡 (阶段内控制)",
            eventType:EventTypeEnum.StartCasting,
            eventCondition:["ActionId:50179"],
            userControl:false)]

        public void P1_众神之像2_恶狠狠毁荡_阶段内控制(Event @event,ScriptAccessory accessory) {
            
            if(!isInPhase1SubPhase(3)) {

                return;

            }
            
            phase1_gravenImage2IsFirstHalf=false;
            debugLog(accessory,"P1 众神之像2: 恶狠狠毁荡读条, 切换到后半判定窗口。");
            
        }
        
        [ScriptMethod(name:"P1 众神之像2 半场刀 (范围)",
            eventType:EventTypeEnum.ObjectEffect,
            eventCondition:["Id1:64"])]

        public void P1_众神之像2_半场刀_范围(Event @event,ScriptAccessory accessory) {
            
            if(!isInPhase1SubPhase(3)) {

                return;

            }
            
            if(!string.Equals(@event["Id2"],"128")) {
                
                return;
                
            }
            
            if(!@event.TrySourcePosition(out var sourcePosition)) {
                
                debugLog(accessory,"P1 众神之像2 半场刀: SourcePosition解析失败。");
                return;
                
            }
            
            Vector3 orientation=ARENA_CENTER;
            string directionName=string.Empty;
            
            if(isNearPosition(sourcePosition,PHASE1_GRAVEN_IMAGE2_LEFT_SLASH_SOURCE_POSITION)) {
                
                orientation=new Vector3(ARENA_CENTER.X-10,ARENA_CENTER.Y,ARENA_CENTER.Z);
                directionName="左";
                
            }
            
            if(isNearPosition(sourcePosition,PHASE1_GRAVEN_IMAGE2_RIGHT_SLASH_SOURCE_POSITION)) {
                
                orientation=new Vector3(ARENA_CENTER.X+10,ARENA_CENTER.Y,ARENA_CENTER.Z);
                directionName="右";
                
            }
            
            if(isNearPosition(orientation,ARENA_CENTER)) {
                
                debugLog(accessory,$"P1 众神之像2 半场刀: 未匹配SourcePosition={sourcePosition}。");
                return;
                
            }
            
            drawHalfArenaFan(accessory,
                orientation,
                accessory.Data.DefaultDangerColor,
                PHASE1_HALF_ARENA_DRAW_DURATION,
                $"P1_众神之像2_半场刀_{directionName}");
            
        }
        
        [ScriptMethod(name:"P1 众神之像3 神像视线 (提示)",
            eventType:EventTypeEnum.ObjectEffect,
            eventCondition:["Id1:64"])]

        public void P1_众神之像3_神像视线_提示(Event @event,ScriptAccessory accessory) {
            
            if(!isInPhase1SubPhase(4)) {

                return;

            }
            
            if(!string.Equals(@event["Id2"],"128")) {
                
                return;
                
            }
            
            if(!@event.TrySourcePosition(out var sourcePosition)) {
                
                debugLog(accessory,"P1 众神之像3 神像视线: SourcePosition解析失败。");
                return;
                
            }
            
            string prompt=string.Empty;
            
            if(isNearPosition(sourcePosition,PHASE1_GRAVEN_IMAGE3_LOOK_AWAY_SOURCE_POSITION)) {
                
                prompt="背对神像";
                
            }
            
            if(isNearPosition(sourcePosition,PHASE1_GRAVEN_IMAGE3_LOOK_AT_SOURCE_POSITION)) {
                
                prompt="面对神像";
                
            }
            
            if(string.IsNullOrWhiteSpace(prompt)) {
                
                debugLog(accessory,$"P1 众神之像3 神像视线: 未匹配SourcePosition={sourcePosition}。");
                return;
                
            }
            
            promptTextAndTts(accessory,prompt,PHASE1_STATUE_GAZE_PROMPT_DURATION);
            
        }
        
        [ScriptMethod(name:"Beta P1 众神之像3 传送放点指路 (状态收集与首段指路)",
            eventType:EventTypeEnum.StatusAdd,
            eventCondition:["StatusID:regex:^(487[6789]|5079|508[012])$"],
            userControl:false)]

        public void Beta_P1_众神之像3_传送放点指路_状态收集与首段指路(Event @event,ScriptAccessory accessory) {
            
            if(!enableBetaPhase1GravenImage3TeleportGuide) {
                
                return;
                
            }
            
            if(!isInMajorPhase(1)) {

                return;

            }
            
            if(!@event.TryTargetId(out var targetId)) {
                
                return;
                
            }
            
            if(targetId!=accessory.Data.Me) {
                
                return;
                
            }
            
            if(!tryGetEventDurationMilliseconds(@event,accessory,out int durationMilliseconds)) {
                
                return;
                
            }
            
            if(!tryGetPhase1GravenImage3TeleportStatusId(@event["StatusID"],out var statusId)) {
                
                return;
                
            }
            
            bool shouldDraw=false;
            Vector3 firstCircle=ARENA_CENTER;
            Vector3 secondCircle=ARENA_CENTER;
            Vector3 firstGuidePosition=ARENA_CENTER;
            int drawGeneration=0;
            string debugMessage=string.Empty;
            
            lock(phase1_gravenImage3TeleportStatusGuideLock) {
                
                if(phase1_gravenImage3TeleportStatusGuideActive) {
                    
                    debugMessage=$"Beta P1 众神之像3 传送放点指路: 已有活跃指路, 忽略新增状态 status={statusId}, duration={durationMilliseconds}, count={phase1_gravenImage3TeleportStatusRecords.Count}。";
                    
                } else {
                    
                    phase1_gravenImage3TeleportStatusRecords.Add(new Phase1GravenImage3TeleportStatusRecord {
                        StatusId=statusId,
                        AddedAtMilliseconds=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        DurationMilliseconds=durationMilliseconds
                    });
                    
                    debugMessage=$"Beta P1 众神之像3 传送放点指路: 收集状态 status={statusId}, duration={durationMilliseconds}, count={phase1_gravenImage3TeleportStatusRecords.Count}。";
                    
                    if(phase1_gravenImage3TeleportStatusRecords.Count==2) {
                        
                        if(tryBuildPhase1GravenImage3TeleportGuidePlan(
                            phase1_gravenImage3TeleportStatusRecords[0],
                            phase1_gravenImage3TeleportStatusRecords[1],
                            out firstCircle,
                            out secondCircle,
                            out phase1_gravenImage3TeleportStatusStep1,
                            out phase1_gravenImage3TeleportStatusStep2,
                            out string caseName)) {
                            
                            phase1_gravenImage3TeleportStatusGuideGeneration++;
                            phase1_gravenImage3TeleportStatusGuideActive=true;
                            drawGeneration=phase1_gravenImage3TeleportStatusGuideGeneration;
                            firstGuidePosition=phase1_gravenImage3TeleportStatusStep1.Position;
                            shouldDraw=true;
                            debugMessage+=$" 匹配{caseName}, first={phase1_gravenImage3TeleportStatusStep1.StatusId}@{phase1_gravenImage3TeleportStatusStep1.Position}, second={phase1_gravenImage3TeleportStatusStep2.StatusId}@{phase1_gravenImage3TeleportStatusStep2.Position}, generation={drawGeneration}。";
                            
                        } else {
                            
                            phase1_gravenImage3TeleportStatusGuideGeneration++;
                            phase1_gravenImage3TeleportStatusGuideActive=false;
                            phase1_gravenImage3TeleportStatusRecords.Clear();
                            debugMessage+=" 未匹配坐标计划, 已清空。";
                            
                        }
                        
                    } else if(phase1_gravenImage3TeleportStatusRecords.Count>2) {
                        
                        phase1_gravenImage3TeleportStatusGuideGeneration++;
                        phase1_gravenImage3TeleportStatusGuideActive=false;
                        phase1_gravenImage3TeleportStatusRecords.Clear();
                        debugMessage+=" 状态数量溢出, 已清空。";
                        
                    }
                    
                }
                
            }
            
            debugLog(accessory,debugMessage);
            
            if(shouldDraw) {
                
                drawPhase1GravenImage3TeleportGuidePlan(accessory,drawGeneration,firstCircle,secondCircle,firstGuidePosition);
                
            }
            
        }
        
        [ScriptMethod(name:"Beta P1 众神之像3 传送放点指路 (状态消失切换)",
            eventType:EventTypeEnum.StatusRemove,
            eventCondition:["StatusID:regex:^(487[6789]|5079|508[012])$"],
            userControl:false)]

        public void Beta_P1_众神之像3_传送放点指路_状态消失切换(Event @event,ScriptAccessory accessory) {
            
            if(!enableBetaPhase1GravenImage3TeleportGuide) {
                
                return;
                
            }
            
            if(!isInMajorPhase(1)) {

                return;

            }
            
            if(!@event.TryTargetId(out var targetId)) {
                
                return;
                
            }
            
            if(targetId!=accessory.Data.Me) {
                
                return;
                
            }
            
            if(!tryGetPhase1GravenImage3TeleportStatusId(@event["StatusID"],out var statusId)) {
                
                return;
                
            }
            
            bool shouldClear=false;
            bool shouldSwitch=false;
            Vector3 nextGuidePosition=ARENA_CENTER;
            int drawGeneration=0;
            string debugMessage=string.Empty;
            
            lock(phase1_gravenImage3TeleportStatusGuideLock) {
                
                int removeIndex=findPhase1GravenImage3TeleportStatusRecordIndex(statusId);
                
                if(removeIndex<0) {
                    
                    debugMessage=$"Beta P1 众神之像3 传送放点指路: 状态消失但未找到记录 status={statusId}, active={phase1_gravenImage3TeleportStatusGuideActive}, count={phase1_gravenImage3TeleportStatusRecords.Count}。";
                    
                } else {
                    
                    phase1_gravenImage3TeleportStatusRecords.RemoveAt(removeIndex);
                    debugMessage=$"Beta P1 众神之像3 传送放点指路: 状态消失 status={statusId}, remaining={phase1_gravenImage3TeleportStatusRecords.Count}。";
                    
                    if(phase1_gravenImage3TeleportStatusGuideActive&&phase1_gravenImage3TeleportStatusRecords.Count<=0) {
                        
                        phase1_gravenImage3TeleportStatusGuideGeneration++;
                        phase1_gravenImage3TeleportStatusGuideActive=false;
                        phase1_gravenImage3TeleportStatusRecords.Clear();
                        phase1_gravenImage3TeleportStatusStep1=default;
                        phase1_gravenImage3TeleportStatusStep2=default;
                        shouldClear=true;
                        debugMessage+=$" 清除全部指路 generation={phase1_gravenImage3TeleportStatusGuideGeneration}。";
                        
                    } else if(phase1_gravenImage3TeleportStatusGuideActive&&phase1_gravenImage3TeleportStatusRecords.Count==1) {
                        
                        var remaining=phase1_gravenImage3TeleportStatusRecords[0];
                        var nextStep=resolvePhase1GravenImage3TeleportStatusStepAfterRemove(remaining);
                        nextGuidePosition=nextStep.Position;
                        drawGeneration=phase1_gravenImage3TeleportStatusGuideGeneration;
                        shouldSwitch=true;
                        debugMessage+=$" 切换到第二段 status={nextStep.StatusId}, position={nextGuidePosition}, generation={drawGeneration}。";
                        
                    }
                    
                }
                
            }
            
            debugLog(accessory,debugMessage);
            
            if(shouldClear) {
                
                accessory.Method.RemoveDraw($"{PHASE1_GRAVEN_IMAGE3_TELEPORT_STATUS_GUIDE_DRAW_PREFIX}_.*");
                return;
                
            }
            
            if(shouldSwitch) {
                
                accessory.Method.RemoveDraw(getPhase1GravenImage3TeleportGuideDrawName(drawGeneration,"Guide_1"));
                drawGuideToPosition(accessory,
                    nextGuidePosition,
                    PHASE1_GRAVEN_IMAGE3_TELEPORT_STATUS_GUIDE_DURATION,
                    getPhase1GravenImage3TeleportGuideDrawName(drawGeneration,"Guide_2"),
                    accessory.Data.DefaultSafeColor);
                
            }
            
        }
        
        #endregion
        
        #region Major_Phase_2

        [ScriptMethod(name:"P2 开场阶段切换",
            eventType:EventTypeEnum.PlayActionTimeline,
            eventCondition:["Id:4565"],
            userControl:false)]

        public void P2_开场阶段切换(Event @event,ScriptAccessory accessory) {
            
            if(!isInMajorPhase(1)||!isPhase2OpeningTimeline(@event)) {

                return;

            }

            switchToPhase2Opening(accessory);

        }
        
        [ScriptMethod(name:"P2 终末双腕 目标圆范围",
            eventType:EventTypeEnum.StartCasting,
            eventCondition:["ActionId:49740"])]

        public void P2_终末双腕_目标圆范围(Event @event,ScriptAccessory accessory) {
            
            if(!isInMajorPhase(2)) {

                return;

            }
            
            if(!@event.TryTargetId(out var targetId)) {
                
                return;
                
            }
            
            drawPhase2TerminalDualArmsTargetCircle(accessory,targetId);

        }
        
        #endregion

        #region Commons

        private bool isInMajorPhase(int expectedMajorPhase) {
            
            return skipPhaseChecks||majorPhase==expectedMajorPhase;
            
        }
        
        private bool isInPhase1SubPhase(int expectedPhase) {
            
            return skipPhaseChecks||(majorPhase==1&&phase==expectedPhase);
            
        }
        
        private bool isPhase2OpeningTimeline(Event @event) {
            
            return string.Equals(@event["SourceDataId"],PHASE2_OPENING_TIMELINE_SOURCE_DATA_ID);
            
        }
        
        private void switchToPhase2Opening(ScriptAccessory accessory) {
            
            majorPhase=2;
            phase=1;
            
            if(!preserveDrawingsWhileSwitchingPhase) {
                
                accessory.Method.RemoveDraw(".*");
                
            }
            
            resetPhase1TransientData();
            resetPhase1GravenImage3TeleportData();
            
            debugLog(accessory,$"P2 开场阶段切换: majorPhase={majorPhase}, phase={phase}。");
            
        }
        
        private void drawPhase2TerminalDualArmsTargetCircle(ScriptAccessory accessory,ulong targetId) {
            
            drawCircleOnObject(accessory,
                targetId,
                PHASE2_TERMINAL_DUAL_ARMS_TARGET_CIRCLE_RADIUS,
                colourOfExtremelyDangerousAttacks.V4.WithW(1),
                PHASE2_TERMINAL_DUAL_ARMS_TARGET_CIRCLE_DURATION,
                $"P2_终末双腕_目标圆范围_{targetId:X}");
            
        }
        
        private void resetPhase1TransientData() {
            
            phase1_pulseCannonDrawn=0;
            resetPhase1GravenImage1TowerData();
            resetPhase1GravenImage1ObfuscationGuideData();
            phase1_gravenImage2IsFirstHalf=true;
            resetPhase1ObfuscationData();
            resetPhase1FlagrantFireIconData();
            resetPhase1FlagrantFireHeadMarkerPairData();
            
        }
        
        private void resetPhase1GravenImage1TowerData() {
            
            phase1_gravenImage1TowerDrawn=0;
            
            lock(phase1_gravenImage1TowerLock) {
                
                phase1_gravenImage1LaserTargetIndexes.Clear();
                phase1_gravenImage1TowerPositions.Clear();
                
            }
            
        }
        
        private void resetPhase1GravenImage1ObfuscationGuideData() {
            
            phase1_gravenImage1ObfuscationGuideDrawn=0;
            
            lock(phase1_gravenImage1ObfuscationGuideLock) {
                
                phase1_gravenImage1IceDiagonalLeftUpRightDown=null;
                phase1_gravenImage1WaveTetherTargets.Clear();
                
            }
            
        }
        
        private void resetPhase1ObfuscationData() {
            
            phase1_isFlagrantFireFake=false;
            phase1_isExpandingFreezeFake=false;
            phase1_isThrummingThunderFake=false;
            phase1_flagrantFireTruthConfirmed.Reset();
            
        }
        
        private void resetPhase1GravenImage3TeleportData() {
            
            phase1_gravenImage3TeleportStatusGuideActive=false;
            phase1_gravenImage3TeleportStatusGuideGeneration++;
            
            lock(phase1_gravenImage3TeleportStatusGuideLock) {
                
                phase1_gravenImage3TeleportStatusRecords.Clear();
                phase1_gravenImage3TeleportStatusStep1=default;
                phase1_gravenImage3TeleportStatusStep2=default;
                
            }
            
        }
        
        private bool tryGetPhase1GravenImage3TeleportStatusId(string? rawStatusId,out uint statusId) {
            
            statusId=0;
            
            if(!uint.TryParse(rawStatusId,out statusId)) {
                
                return false;
                
            }
            
            return statusId is 4876 or 4877 or 4878 or 4879 or 5079 or 5080 or 5081 or 5082;
            
        }
        
        private bool tryBuildPhase1GravenImage3TeleportGuidePlan(
            Phase1GravenImage3TeleportStatusRecord first,
            Phase1GravenImage3TeleportStatusRecord second,
            out Vector3 circle1,
            out Vector3 circle2,
            out Phase1GravenImage3TeleportStatusStep step1,
            out Phase1GravenImage3TeleportStatusStep step2,
            out string caseName) {
            
            circle1=ARENA_CENTER;
            circle2=ARENA_CENTER;
            step1=default;
            step2=default;
            caseName=string.Empty;
            
            if(first.StatusId==second.StatusId) {
                
                return tryBuildPhase1GravenImage3DuplicateTeleportGuide(first.StatusId,out circle1,out circle2,out step1,out step2,out caseName);
                
            }
            
            if(tryBuildPhase1GravenImage3MixedTeleportGuide(first,second,4878,new Vector3(108,0,100),5080,new Vector3(114,0,100),"情况5",out circle1,out circle2,out step1,out step2,out caseName)) {
                
                return true;
                
            }
            
            if(tryBuildPhase1GravenImage3MixedTeleportGuide(first,second,4879,new Vector3(92,0,100),5079,new Vector3(86,0,100),"情况6",out circle1,out circle2,out step1,out step2,out caseName)) {
                
                return true;
                
            }
            
            if(tryBuildPhase1GravenImage3MixedTeleportGuide(first,second,4876,new Vector3(100,0,92),5081,new Vector3(100,0,86),"情况7",out circle1,out circle2,out step1,out step2,out caseName)) {
                
                return true;
                
            }
            
            if(tryBuildPhase1GravenImage3MixedTeleportGuide(first,second,4877,new Vector3(100,0,108),5082,new Vector3(100,0,114),"情况8",out circle1,out circle2,out step1,out step2,out caseName)) {
                
                return true;
                
            }
            
            return false;
            
        }
        
        private bool tryBuildPhase1GravenImage3DuplicateTeleportGuide(
            uint statusId,
            out Vector3 circle1,
            out Vector3 circle2,
            out Phase1GravenImage3TeleportStatusStep step1,
            out Phase1GravenImage3TeleportStatusStep step2,
            out string caseName) {
            
            circle1=ARENA_CENTER;
            circle2=ARENA_CENTER;
            step1=default;
            step2=default;
            caseName=string.Empty;
            
            float rotation;
            int caseIndex;
            
            switch(statusId) {
                
                case 4876:
                    rotation=0;
                    caseIndex=1;
                    break;
                
                case 4877:
                    rotation=float.Pi;
                    caseIndex=2;
                    break;
                
                case 4878:
                    rotation=float.Pi/2;
                    caseIndex=3;
                    break;
                
                case 4879:
                    rotation=-float.Pi/2;
                    caseIndex=4;
                    break;
                
                default:
                    return false;
                
            }
            
            Vector3 nearPosition=rotatePositionAroundArenaCenter(new Vector3(94,0,108),rotation);
            Vector3 farPosition=rotatePositionAroundArenaCenter(new Vector3(94,0,114),rotation);
            
            circle1=nearPosition;
            circle2=farPosition;
            step1=new Phase1GravenImage3TeleportStatusStep { StatusId=statusId, Position=farPosition };
            step2=new Phase1GravenImage3TeleportStatusStep { StatusId=statusId, Position=nearPosition };
            caseName=$"情况{caseIndex} {statusId}+{statusId}";
            return true;
            
        }
        
        private bool tryBuildPhase1GravenImage3MixedTeleportGuide(
            Phase1GravenImage3TeleportStatusRecord first,
            Phase1GravenImage3TeleportStatusRecord second,
            uint statusA,
            Vector3 positionA,
            uint statusB,
            Vector3 positionB,
            string caseLabel,
            out Vector3 circle1,
            out Vector3 circle2,
            out Phase1GravenImage3TeleportStatusStep step1,
            out Phase1GravenImage3TeleportStatusStep step2,
            out string caseName) {
            
            circle1=positionA;
            circle2=positionB;
            step1=default;
            step2=default;
            caseName=string.Empty;
            
            if(!isStatusPair(first.StatusId,second.StatusId,statusA,statusB)) {
                
                return false;
                
            }
            
            var recordA=first.StatusId==statusA?first:second;
            var recordB=first.StatusId==statusB?first:second;
            var stepA=new Phase1GravenImage3TeleportStatusStep { StatusId=statusA, Position=positionA };
            var stepB=new Phase1GravenImage3TeleportStatusStep { StatusId=statusB, Position=positionB };
            
            if(getPhase1GravenImage3TeleportStatusExpiresAt(recordA)<=getPhase1GravenImage3TeleportStatusExpiresAt(recordB)) {
                
                step1=stepA;
                step2=stepB;
                
            } else {
                
                step1=stepB;
                step2=stepA;
                
            }
            
            caseName=$"{caseLabel} {first.StatusId}+{second.StatusId}";
            return true;
            
        }
        
        private bool isStatusPair(uint first,uint second,uint statusA,uint statusB) {
            
            return (first==statusA&&second==statusB)||(first==statusB&&second==statusA);
            
        }
        
        private long getPhase1GravenImage3TeleportStatusExpiresAt(Phase1GravenImage3TeleportStatusRecord record) {
            
            return record.AddedAtMilliseconds+record.DurationMilliseconds;
            
        }
        
        private int findPhase1GravenImage3TeleportStatusRecordIndex(uint statusId) {
            
            for(int i=0;i<phase1_gravenImage3TeleportStatusRecords.Count;i++) {
                
                if(phase1_gravenImage3TeleportStatusRecords[i].StatusId==statusId) {
                    
                    return i;
                    
                }
                
            }
            
            return -1;
            
        }
        
        private Phase1GravenImage3TeleportStatusStep resolvePhase1GravenImage3TeleportStatusStepAfterRemove(Phase1GravenImage3TeleportStatusRecord remaining) {
            
            if(phase1_gravenImage3TeleportStatusStep1.StatusId==phase1_gravenImage3TeleportStatusStep2.StatusId) {
                
                return phase1_gravenImage3TeleportStatusStep2;
                
            }
            
            return remaining.StatusId==phase1_gravenImage3TeleportStatusStep2.StatusId?phase1_gravenImage3TeleportStatusStep2:phase1_gravenImage3TeleportStatusStep1;
            
        }
        
        private void drawPhase1GravenImage3TeleportGuidePlan(ScriptAccessory accessory,int generation,Vector3 circle1,Vector3 circle2,Vector3 firstGuidePosition) {
            
            drawCircleAtPosition(accessory,
                circle1,
                PHASE1_GRAVEN_IMAGE3_TELEPORT_STATUS_GUIDE_RADIUS,
                accessory.Data.DefaultSafeColor,
                PHASE1_GRAVEN_IMAGE3_TELEPORT_STATUS_GUIDE_DURATION,
                getPhase1GravenImage3TeleportGuideDrawName(generation,"Circle_1"),
                drawMode:DrawModeEnum.Imgui);
            
            drawCircleAtPosition(accessory,
                circle2,
                PHASE1_GRAVEN_IMAGE3_TELEPORT_STATUS_GUIDE_RADIUS,
                accessory.Data.DefaultSafeColor,
                PHASE1_GRAVEN_IMAGE3_TELEPORT_STATUS_GUIDE_DURATION,
                getPhase1GravenImage3TeleportGuideDrawName(generation,"Circle_2"),
                drawMode:DrawModeEnum.Imgui);
            
            drawGuideToPosition(accessory,
                firstGuidePosition,
                PHASE1_GRAVEN_IMAGE3_TELEPORT_STATUS_GUIDE_DURATION,
                getPhase1GravenImage3TeleportGuideDrawName(generation,"Guide_1"),
                accessory.Data.DefaultSafeColor);
            
        }
        
        private string getPhase1GravenImage3TeleportGuideDrawName(int generation,string suffix) {
            
            return $"{PHASE1_GRAVEN_IMAGE3_TELEPORT_STATUS_GUIDE_DRAW_PREFIX}_{generation}_{suffix}";
            
        }
        
        private Vector3 rotatePositionAroundArenaCenter(Vector3 position,float radians) {
            
            float x=position.X-ARENA_CENTER.X;
            float z=position.Z-ARENA_CENTER.Z;
            float sin=MathF.Sin(radians);
            float cos=MathF.Cos(radians);
            
            return new Vector3(
                ARENA_CENTER.X+x*cos+z*sin,
                position.Y,
                ARENA_CENTER.Z-x*sin+z*cos);
            
        }
        
        private void collectPhase1ObfuscationData(Event @event,ScriptAccessory accessory) {
            
            string iconId=@event["Id"];
            
            switch(iconId) {
                
                case "02A1":
                    
                    phase1_isFlagrantFireFake=true;
                    phase1_flagrantFireTruthConfirmed.Set();
                    debugLog(accessory,"P1 玄乎乎魔法: 呼啦啦爆炎为假。");
                    break;
                
                case "02A2":
                    
                    phase1_isFlagrantFireFake=false;
                    phase1_flagrantFireTruthConfirmed.Set();
                    debugLog(accessory,"P1 玄乎乎魔法: 呼啦啦爆炎为真。");
                    break;
                
                case "02A3":
                    
                    phase1_isExpandingFreezeFake=true;
                    debugLog(accessory,"P1 玄乎乎魔法: 扩大大冰封为假。");
                    break;
                
                case "02A4":
                    
                    phase1_isExpandingFreezeFake=false;
                    debugLog(accessory,"P1 玄乎乎魔法: 扩大大冰封为真。");
                    break;
                
                case "02A5":
                    
                    phase1_isThrummingThunderFake=true;
                    debugLog(accessory,"P1 玄乎乎魔法: 劈啪啪暴雷为假。");
                    break;
                
                case "02A6":
                    
                    phase1_isThrummingThunderFake=false;
                    debugLog(accessory,"P1 玄乎乎魔法: 劈啪啪暴雷为真。");
                    break;
                
            }
            
        }
        
        private void resetPhase1FlagrantFireData() {
            
            phase1_isFlagrantFireFake=false;
            phase1_flagrantFireTruthConfirmed.Reset();
            resetPhase1FlagrantFireIconData();
            resetPhase1FlagrantFireHeadMarkerPairData();
            
        }
        
        private void resetPhase1FlagrantFireIconData() {
            
            phase1_isFlagrantFireStackIcon=false;
            phase1_flagrantFireIconConfirmed.Reset();
            
            lock(phase1_flagrantFireTargetsLock) {
                
                phase1_flagrantFireTargets.Clear();
                
            }
            
        }
        
        private void resetPhase1FlagrantFireHeadMarkerPairData() {
            
            lock(phase1_flagrantFireHeadMarkerPairLock) {
                
                phase1_pendingFlagrantFirePlayerIcon=string.Empty;
                phase1_pendingFlagrantFireObfuscationIcon=string.Empty;
                phase1_flagrantFireHeadMarkerPairGeneration++;
                
            }
            
        }
        
        private void capturePhase1FlagrantFireHeadMarkerPair(ScriptAccessory accessory,string iconId,bool isPlayerIcon) {
            
            string prompt=string.Empty;
            string debugMessage=string.Empty;
            int waitGeneration=0;
            
            lock(phase1_flagrantFireHeadMarkerPairLock) {
                
                if(isPlayerIcon) {
                    
                    phase1_pendingFlagrantFirePlayerIcon=iconId;
                    
                } else {
                    
                    phase1_pendingFlagrantFireObfuscationIcon=iconId;
                    
                }
                
                phase1_flagrantFireHeadMarkerPairGeneration++;
                
                if(tryGetPhase1FlagrantFirePrompt(
                    phase1_pendingFlagrantFireObfuscationIcon,
                    phase1_pendingFlagrantFirePlayerIcon,
                    out prompt)) {
                    
                    debugMessage=$"P1 呼啦啦爆炎: 头标配对成功 bossIcon={phase1_pendingFlagrantFireObfuscationIcon}, playerIcon={phase1_pendingFlagrantFirePlayerIcon}, prompt={prompt}。";
                    phase1_pendingFlagrantFirePlayerIcon=string.Empty;
                    phase1_pendingFlagrantFireObfuscationIcon=string.Empty;
                    phase1_flagrantFireHeadMarkerPairGeneration++;
                    
                } else {
                    
                    waitGeneration=phase1_flagrantFireHeadMarkerPairGeneration;
                    debugMessage=$"P1 呼啦啦爆炎: 等待头标配对 bossIcon={valueOrDash(phase1_pendingFlagrantFireObfuscationIcon)}, playerIcon={valueOrDash(phase1_pendingFlagrantFirePlayerIcon)}, generation={waitGeneration}。";
                    
                }
                
            }
            
            debugLog(accessory,debugMessage);
            
            if(!string.IsNullOrWhiteSpace(prompt)) {
                
                promptTextAndTts(accessory,prompt,PHASE1_FLAGRANT_FIRE_DRAW_DURATION);
                return;
                
            }
            
            _=clearPhase1FlagrantFireHeadMarkerPairAfterDelay(accessory,waitGeneration);
            
        }
        
        private async Task clearPhase1FlagrantFireHeadMarkerPairAfterDelay(ScriptAccessory accessory,int generation) {
            
            await Task.Delay(1000);
            
            string debugMessage=string.Empty;
            
            lock(phase1_flagrantFireHeadMarkerPairLock) {
                
                if(phase1_flagrantFireHeadMarkerPairGeneration!=generation) {
                    
                    return;
                    
                }
                
                if(!string.IsNullOrWhiteSpace(phase1_pendingFlagrantFirePlayerIcon)||!string.IsNullOrWhiteSpace(phase1_pendingFlagrantFireObfuscationIcon)) {
                    
                    debugMessage=$"P1 呼啦啦爆炎: 头标配对超时 bossIcon={valueOrDash(phase1_pendingFlagrantFireObfuscationIcon)}, playerIcon={valueOrDash(phase1_pendingFlagrantFirePlayerIcon)}, generation={generation}。";
                    phase1_pendingFlagrantFirePlayerIcon=string.Empty;
                    phase1_pendingFlagrantFireObfuscationIcon=string.Empty;
                    phase1_flagrantFireHeadMarkerPairGeneration++;
                    
                }
                
            }
            
            if(!string.IsNullOrWhiteSpace(debugMessage)) {
                
                debugLog(accessory,debugMessage);
                
            }
            
        }
        
        private bool tryGetPhase1FlagrantFirePrompt(string obfuscationIconId,string playerIconId,out string prompt) {
            
            prompt=string.Empty;
            
            if(obfuscationIconId=="02A2") {
                
                if(playerIconId=="0080") {
                    
                    prompt="分摊";
                    
                }
                
                if(playerIconId=="007F") {
                    
                    prompt="分散";
                    
                }
                
            }
            
            if(obfuscationIconId=="02A1") {
                
                if(playerIconId=="007F") {
                    
                    prompt="分摊";
                    
                }
                
                if(playerIconId=="0080") {
                    
                    prompt="分散";
                    
                }
                
            }
            
            return !string.IsNullOrWhiteSpace(prompt);
            
        }
        
        private void collectPhase1FlagrantFireIcon(Event @event,ScriptAccessory accessory) {
            
            if(!convertObjectIdToDecimal(@event["TargetId"],out var targetId)) {
                
                return;
                
            }

            string iconId=@event["Id"];
            
            lock(phase1_flagrantFireTargetsLock) {
                
                phase1_flagrantFireTargets.Add(targetId);

                if(phase1_flagrantFireTargets.Count==2&&string.Equals(iconId,"0080")) {

                    phase1_isFlagrantFireStackIcon=true;
                    phase1_flagrantFireIconConfirmed.Set();
                    debugLog(accessory,$"P1 呼啦啦爆炎: 收集到双人分摊图标, targets={string.Join(",",phase1_flagrantFireTargets)}。");

                }
                
                if(phase1_flagrantFireTargets.Count==8&&string.Equals(iconId,"007F")) {

                    phase1_isFlagrantFireStackIcon=false;
                    phase1_flagrantFireIconConfirmed.Set();
                    debugLog(accessory,$"P1 呼啦啦爆炎: 收集到八人分散图标, targets={string.Join(",",phase1_flagrantFireTargets)}。");

                }
                
            }
            
        }
        
        private void drawCircleOnObject(ScriptAccessory accessory,ulong objectId,float radius,Vector4 colour,int duration,string name,int delay=0,DrawModeEnum drawMode=DrawModeEnum.Default) {
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();
            
            currentProperties.Name=name;
            currentProperties.Scale=new(radius);
            currentProperties.Owner=objectId;
            currentProperties.Color=colour;
            currentProperties.Delay=delay;
            currentProperties.DestoryAt=duration;
            
            accessory.Method.SendDraw(drawMode,DrawTypeEnum.Circle,currentProperties);
            
        }
        
        private void drawCircleAtPosition(ScriptAccessory accessory,Vector3 position,float radius,Vector4 colour,int duration,string name,int delay=0,DrawModeEnum drawMode=DrawModeEnum.Default) {
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();
            
            currentProperties.Name=name;
            currentProperties.Scale=new(radius);
            currentProperties.Position=position;
            currentProperties.Color=colour;
            currentProperties.Delay=delay;
            currentProperties.DestoryAt=duration;
            
            accessory.Method.SendDraw(drawMode,DrawTypeEnum.Circle,currentProperties);
            
        }
        
        private void drawGuideToPosition(ScriptAccessory accessory,Vector3 targetPosition,int duration,string name,Vector4 colour,int delay=0) {
            
            var waypoint=accessory.waypointToPosition(
                targetPosition,
                duration,
                delay,
                name,
                colour);
            
            accessory.Method.SendDraw(DrawModeEnum.Imgui,DrawTypeEnum.Displacement,waypoint);
            
        }
        
        private void drawFanOnObject(ScriptAccessory accessory,ulong objectId,float radius,float radian,Vector4 colour,int duration,string name,DrawModeEnum drawMode=DrawModeEnum.Imgui) {
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();
            
            currentProperties.Name=name;
            currentProperties.Scale=new(radius);
            currentProperties.Radian=radian;
            currentProperties.Owner=objectId;
            currentProperties.Color=colour;
            currentProperties.DestoryAt=duration;
            
            accessory.Method.SendDraw(drawMode,DrawTypeEnum.Fan,currentProperties);
            
        }
        
        private void drawRectOnObject(ScriptAccessory accessory,ulong objectId,Vector2 scale,Vector4 colour,int duration,string name,DrawModeEnum drawMode=DrawModeEnum.Default) {
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();
            
            currentProperties.Name=name;
            currentProperties.Scale=scale;
            currentProperties.Owner=objectId;
            currentProperties.Color=colour;
            currentProperties.DestoryAt=duration;
            
            accessory.Method.SendDraw(drawMode,DrawTypeEnum.Rect,currentProperties);
            
        }
        
        private string getPhase1ChainTrapDrawName(ulong targetId) {
            
            return $"P1_众神之像1_连环环陷阱_范围_{targetId:X}";
            
        }
        
        private string getPhase1ChainTrapKnockbackGuideDrawName(ulong targetId) {
            
            return $"Beta_P1_众神之像1_连环环陷阱_击退指示_{targetId:X}";
            
        }
        
        private void drawKnockbackGuideFromTrap(ScriptAccessory accessory,ulong trapTargetId,int duration,string name,int delay=0) {
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();
            
            currentProperties.Name=name;
            currentProperties.Scale=new Vector2(2,14);
            currentProperties.Owner=accessory.Data.Me;
            currentProperties.TargetObject=trapTargetId;
            currentProperties.Rotation=float.Pi;
            currentProperties.FadeCentreObject=trapTargetId;
            currentProperties.FadeDistance=PHASE1_CHAIN_TRAP_RADIUS;
            currentProperties.Color=colourOfDirectionIndicators.V4.WithW(1);
            currentProperties.Delay=delay;
            currentProperties.DestoryAt=duration;
            
            accessory.Method.SendDraw(DrawModeEnum.Imgui,DrawTypeEnum.Displacement,currentProperties);
            
        }
        
        private bool tryGetEventDurationMilliseconds(Event @event,ScriptAccessory accessory,out int durationMilliseconds) {
            
            durationMilliseconds=0;
            string rawDuration=@event["DurationMilliseconds"];
            
            if(string.IsNullOrWhiteSpace(rawDuration)) {
                
                return false;
                
            }
            
            try {
                
                durationMilliseconds=JsonConvert.DeserializeObject<int>(rawDuration);
                
            } catch(Exception exception) {
                
                if(!convertStringToSignedInteger(rawDuration,out durationMilliseconds)) {
                    
                    accessory.Log.Error($"DurationMilliseconds parse failed: {exception.Message}");
                    return false;
                    
                }
                
            }
            
            if(durationMilliseconds<=0||durationMilliseconds>MAXIMUM_DURATION) {
                
                debugLog(accessory,$"DurationMilliseconds out of range: {durationMilliseconds}。");
                return false;
                
            }
            
            return true;
            
        }
        
        private void getLastWindowDrawTiming(int totalDurationMilliseconds,double maxVisibleSeconds,out int delayMilliseconds,out int visibleDurationMilliseconds) {
            
            delayMilliseconds=0;
            visibleDurationMilliseconds=totalDurationMilliseconds;
            
            int maxVisibleMilliseconds=(int)(maxVisibleSeconds*1000);
            
            if(maxVisibleMilliseconds<=0||totalDurationMilliseconds<=maxVisibleMilliseconds) {
                
                return;
                
            }
            
            delayMilliseconds=totalDurationMilliseconds-maxVisibleMilliseconds;
            visibleDurationMilliseconds=maxVisibleMilliseconds;
            
        }
        
        private void drawFixedArrowOnMe(ScriptAccessory accessory,Vector2 scale,float rotation,Vector4 colour,int duration,string name) {
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();
            
            currentProperties.Name=name;
            currentProperties.Scale=scale;
            currentProperties.Owner=accessory.Data.Me;
            currentProperties.FixRotation=true;
            currentProperties.Rotation=rotation;
            currentProperties.Color=colour;
            currentProperties.DestoryAt=duration;
            
            accessory.Method.SendDraw(DrawModeEnum.Imgui,DrawTypeEnum.Arrow,currentProperties);
            
        }
        
        private void drawDelayedRectToObject(ScriptAccessory accessory,Vector3 startPosition,ulong targetId,Vector2 scale,Vector4 colour,int delay,int duration,string name) {
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();
            
            currentProperties.Name=name;
            currentProperties.Scale=scale;
            currentProperties.Position=startPosition;
            currentProperties.TargetObject=targetId;
            currentProperties.Color=colour;
            currentProperties.Delay=delay;
            currentProperties.DestoryAt=duration;
            
            accessory.Method.SendDraw(DrawModeEnum.Default,DrawTypeEnum.Rect,currentProperties);
            
        }
        
        private async Task resolvePhase1GravenImage1ObfuscationGuideAfterDelay(ScriptAccessory accessory) {
            
            bool? diagonalLeftUpRightDown=null;
            HashSet<ulong> tetherTargets=new HashSet<ulong>();
            
            for(int i=0;i<PHASE1_GRAVEN_IMAGE1_OBFUSCATION_DATA_WAIT_RETRIES;i++) {
                
                await Task.Delay(PHASE1_GRAVEN_IMAGE1_OBFUSCATION_DATA_WAIT_INTERVAL);
                
                lock(phase1_gravenImage1ObfuscationGuideLock) {
                    
                    diagonalLeftUpRightDown=phase1_gravenImage1IceDiagonalLeftUpRightDown;
                    tetherTargets=new HashSet<ulong>(phase1_gravenImage1WaveTetherTargets);
                    
                }
                
                if(diagonalLeftUpRightDown.HasValue&&tetherTargets.Count==4) {
                    
                    break;
                    
                }
                
            }
            
            if(!diagonalLeftUpRightDown.HasValue) {
                
                debugLog(accessory,"P1 众神之像1 玄乎乎魔法个人指路: 未收集到冰方向, 跳过指路。");
                return;
                
            }
            
            if(tetherTargets.Count!=4) {
                
                debugLog(accessory,$"P1 众神之像1 玄乎乎魔法个人指路: 波动弹连线数量异常 count={tetherTargets.Count}, 跳过指路。");
                return;
                
            }
            
            resolvePhase1GravenImage1ObfuscationGuide(accessory,diagonalLeftUpRightDown.Value,tetherTargets);
            
        }
        
        private void resolvePhase1GravenImage1ObfuscationGuide(ScriptAccessory accessory,bool diagonalLeftUpRightDown,HashSet<ulong> tetherTargets) {
            
            int myIndex=accessory.getMyIndex();
            
            if(!isLegalPartyIndex(myIndex)) {
                
                debugLog(accessory,"P1 众神之像1 玄乎乎魔法个人指路: 无法解析自己的小队序号。");
                return;
                
            }
            
            Vector3 firstGuidePosition=getPhase1GravenImage1FirstObfuscationGuidePosition(accessory.Data.Me,diagonalLeftUpRightDown,tetherTargets);
            
            if(!tryGetPhase1GravenImage1SecondObfuscationGuidePosition(accessory,diagonalLeftUpRightDown,tetherTargets,out Vector3 secondGuidePosition,out string sideName,out int order)) {
                
                debugLog(accessory,$"P1 众神之像1 玄乎乎魔法个人指路: 第二段分配失败 myIndex={myIndex}, tetherTargets={string.Join(",",tetherTargets)}。");
                return;
                
            }
            
            drawGuideToPosition(accessory,
                firstGuidePosition,
                PHASE1_GRAVEN_IMAGE1_OBFUSCATION_FIRST_GUIDE_DURATION,
                $"Beta_P1_众神之像1_玄乎乎魔法_第一段指路_{myIndex}",
                accessory.Data.DefaultSafeColor);
            
            drawGuideToPosition(accessory,
                secondGuidePosition,
                PHASE1_GRAVEN_IMAGE1_OBFUSCATION_SECOND_GUIDE_DURATION,
                $"Beta_P1_众神之像1_玄乎乎魔法_第二段指路_{myIndex}",
                accessory.Data.DefaultSafeColor,
                PHASE1_GRAVEN_IMAGE1_OBFUSCATION_SECOND_GUIDE_DELAY);
            
            debugLog(accessory,$"P1 众神之像1 玄乎乎魔法个人指路: myIndex={myIndex}, diagonal={(diagonalLeftUpRightDown?"左上右下":"左下右上")}, first={firstGuidePosition}, second={secondGuidePosition}, side={sideName}, order={order}。");
            
        }
        
        private Vector3 getPhase1GravenImage1FirstObfuscationGuidePosition(ulong playerId,bool diagonalLeftUpRightDown,HashSet<ulong> tetherTargets) {
            
            Vector3 coveredUpperPoint=diagonalLeftUpRightDown?PHASE1_GRAVEN_IMAGE1_UPPER_LEFT_POINT:PHASE1_GRAVEN_IMAGE1_UPPER_RIGHT_POINT;
            Vector3 uncoveredUpperPoint=diagonalLeftUpRightDown?PHASE1_GRAVEN_IMAGE1_UPPER_RIGHT_POINT:PHASE1_GRAVEN_IMAGE1_UPPER_LEFT_POINT;
            
            return tetherTargets.Contains(playerId)?coveredUpperPoint:uncoveredUpperPoint;
            
        }
        
        private bool tryGetPhase1GravenImage1SecondObfuscationGuidePosition(
            ScriptAccessory accessory,
            bool diagonalLeftUpRightDown,
            HashSet<ulong> tetherTargets,
            out Vector3 guidePosition,
            out string sideName,
            out int order) {
            
            guidePosition=ARENA_CENTER;
            sideName=string.Empty;
            order=-1;
            
            var rightSideMembers=accessory.Data.PartyList
                .Where(playerId => getPhase1GravenImage1FirstObfuscationGuidePosition(playerId,diagonalLeftUpRightDown,tetherTargets).X>ARENA_CENTER.X)
                .OrderBy(playerId => accessory.Data.PartyList.IndexOf(playerId))
                .ToList();
            
            var leftSideMembers=accessory.Data.PartyList
                .Where(playerId => getPhase1GravenImage1FirstObfuscationGuidePosition(playerId,diagonalLeftUpRightDown,tetherTargets).X<ARENA_CENTER.X)
                .OrderBy(playerId => accessory.Data.PartyList.IndexOf(playerId))
                .ToList();
            
            int rightOrder=rightSideMembers.IndexOf(accessory.Data.Me);
            
            if(0<=rightOrder&&rightOrder<PHASE1_GRAVEN_IMAGE1_RIGHT_SIDE_GUIDE_POINTS.Length) {
                
                guidePosition=PHASE1_GRAVEN_IMAGE1_RIGHT_SIDE_GUIDE_POINTS[rightOrder];
                sideName="右半场";
                order=rightOrder;
                return true;
                
            }
            
            int leftOrder=leftSideMembers.IndexOf(accessory.Data.Me);
            
            if(0<=leftOrder&&leftOrder<PHASE1_GRAVEN_IMAGE1_LEFT_SIDE_GUIDE_POINTS.Length) {
                
                guidePosition=PHASE1_GRAVEN_IMAGE1_LEFT_SIDE_GUIDE_POINTS[leftOrder];
                sideName="左半场";
                order=leftOrder;
                return true;
                
            }
            
            return false;
            
        }
        
        private bool isPhase1GravenImage1IceDiagonalLeftUpRightDown(float rotation) {
            
            double normalizedDegrees=((rotation*180.0/Math.PI)%360.0+360.0)%360.0;
            double leftUpRightDownDistance=Math.Min(
                circularDegreeDistance(normalizedDegrees,45),
                circularDegreeDistance(normalizedDegrees,225));
            double leftDownRightUpDistance=Math.Min(
                circularDegreeDistance(normalizedDegrees,135),
                circularDegreeDistance(normalizedDegrees,315));
            
            return leftUpRightDownDistance<=leftDownRightUpDistance;
            
        }
        
        private static double circularDegreeDistance(double a,double b) {
            
            double difference=Math.Abs(a-b)%360.0;
            
            return difference<=180.0?difference:360.0-difference;
            
        }
        
        private void resolvePhase1GravenImage1Tower(ScriptAccessory accessory,List<int> laserTargetIndexes,List<Vector3> towerPositions) {
            
            int myIndex=accessory.getMyIndex();
            
            if(!isLegalPartyIndex(myIndex)) {
                
                debugLog(accessory,"P1 众神之像1 单人塔: 无法解析自己的小队序号。");
                return;
                
            }
            
            if(towerPositions.Count<4) {
                
                debugLog(accessory,$"P1 众神之像1 单人塔: 塔点数量不足 count={towerPositions.Count}。");
                return;
                
            }
            
            var sortedTowerPositions=towerPositions
                .OrderBy(position => position.X)
                .ThenBy(position => position.Z)
                .Take(4)
                .ToList();
            
            if(laserTargetIndexes.Contains(myIndex)) {
                
                for(int i=0;i<sortedTowerPositions.Count;i++) {
                    
                    drawCircleAtPosition(accessory,
                        sortedTowerPositions[i],
                        PHASE1_SINGLE_TOWER_RADIUS,
                        accessory.Data.DefaultDangerColor,
                        PHASE1_SINGLE_TOWER_DRAW_DURATION,
                        $"P1_众神之像1_单人塔_非自己_{i}",
                        drawMode:DrawModeEnum.Default);
                    
                }
                
                return;
                
            }
            
            var towerSoakers=PHASE1_GRAVEN_IMAGE1_TOWER_PRIORITY
                .Where(partyIndex => !laserTargetIndexes.Contains(partyIndex))
                .ToList();
            
            int assignedTowerIndex=towerSoakers.IndexOf(myIndex);
            
            if(assignedTowerIndex<0||assignedTowerIndex>=sortedTowerPositions.Count) {
                
                debugLog(accessory,$"P1 众神之像1 单人塔: 分配失败 myIndex={myIndex}, towerSoakers={string.Join(",",towerSoakers)}, laserTargets={string.Join(",",laserTargetIndexes)}。");
                return;
                
            }
            
            Vector3 assignedPosition=sortedTowerPositions[assignedTowerIndex];
            
            drawCircleAtPosition(accessory,
                assignedPosition,
                PHASE1_SINGLE_TOWER_RADIUS,
                accessory.Data.DefaultSafeColor,
                PHASE1_SINGLE_TOWER_DRAW_DURATION,
                $"P1_众神之像1_单人塔_自己_{myIndex}",
                drawMode:DrawModeEnum.Imgui);
            
            var waypoint=accessory.waypointToPosition(
                assignedPosition,
                PHASE1_SINGLE_TOWER_DRAW_DURATION,
                name:$"P1_众神之像1_单人塔_指路_{myIndex}",
                colour:accessory.Data.DefaultSafeColor);
            accessory.Method.SendDraw(DrawModeEnum.Imgui,DrawTypeEnum.Displacement,waypoint);
            
            debugLog(accessory,$"P1 众神之像1 单人塔: myIndex={myIndex}, towerIndex={assignedTowerIndex}, position={assignedPosition}。");
            
        }
        
        private void drawHalfArenaFan(ScriptAccessory accessory,Vector3 orientation,Vector4 colour,int duration,string name) {
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();
            
            currentProperties.Name=name;
            currentProperties.Scale=new(100);
            currentProperties.Radian=float.Pi;
            currentProperties.Position=ARENA_CENTER;
            currentProperties.TargetPosition=orientation;
            currentProperties.Color=colour;
            currentProperties.DestoryAt=duration;
            
            accessory.Method.SendDraw(DrawModeEnum.Default,DrawTypeEnum.Fan,currentProperties);
            
        }
        
        private void promptTextAndTts(ScriptAccessory accessory,string prompt,int duration) {
            
            if(enablePrompts) {
                
                accessory.Method.TextInfo(prompt,duration);
                
            }
            
            accessory.tts(prompt,enableVanillaTts,enableDailyRoutinesTts);
            
        }
        
        private bool isNearPosition(Vector3 position,Vector3 expectedPosition) {
            
            return isNearPosition(position,expectedPosition,POSITION_MATCH_TOLERANCE);
            
        }
        
        private bool isNearPosition(Vector3 position,Vector3 expectedPosition,float tolerance) {
            
            return Vector3.Distance(position,expectedPosition)<=tolerance;
            
        }
        
        private void debugLog(ScriptAccessory accessory,string message) {
            
            if(!enableDebugLogging) {
                
                return;
                
            }
            
            accessory.Log.Debug(message);
            
        }
        
        private bool tryNormalizeIconId(string? rawIconId,out string iconId) {
            
            iconId=string.Empty;
            
            if(string.IsNullOrWhiteSpace(rawIconId)) {
                
                return false;
                
            }
            
            string cleanIconId=rawIconId.Trim();
            cleanIconId=cleanIconId.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?cleanIconId.Substring(2):cleanIconId;
            cleanIconId=cleanIconId.ToUpperInvariant();
            
            if(!Regex.IsMatch(cleanIconId,"^[0-9A-F]+$")) {
                
                return false;
                
            }
            
            if(!uint.TryParse(cleanIconId,System.Globalization.NumberStyles.HexNumber,null,out uint parsedIconId)) {
                
                return false;
                
            }
            
            iconId=parsedIconId.ToString("X4",System.Globalization.CultureInfo.InvariantCulture);
            return true;
            
        }
        
        private static string valueOrDash(string? value) {
            
            return string.IsNullOrWhiteSpace(value)?"-":value;
            
        }
        
        public static bool convertObjectIdToDecimal(string? rawObjectId,out ulong result) {
            
            result=0;

            if(string.IsNullOrWhiteSpace(rawObjectId)) {
                
                return false;
                
            }

            string objectId=rawObjectId.Trim();
            
            objectId=objectId.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?objectId.Substring(2):objectId;
            
            return ulong.TryParse(objectId,System.Globalization.NumberStyles.HexNumber,null,out result);
            
        }
        
        public static bool convertStringToSignedInteger(string? rawString,out int result) {
    
            result=0;

            if(string.IsNullOrWhiteSpace(rawString)) {
        
                return false;
        
            }

            string cleanString=rawString.Trim();

            return int.TryParse(cleanString,System.Globalization.NumberStyles.Integer,null,out result);
    
        }
        
        public static int discretizePosition(Vector3 position,Vector3 center,int numberOfDirections,bool diagonalSplit=true) {

            if(diagonalSplit) {
                
                return (int)(
                
                    (Math.Round(
                    
                        (numberOfDirections/2.0d)-(numberOfDirections/2.0d)*Math.Atan2(position.X-center.X,position.Z-center.Z)/Math.PI
                    
                    )%numberOfDirections+numberOfDirections)%numberOfDirections
                
                );
                
            }

            else {
                
                return (int)(
                
                    (Math.Floor(
                    
                        (numberOfDirections/2.0d)-(numberOfDirections/2.0d)*Math.Atan2(position.X-center.X,position.Z-center.Z)/Math.PI
                    
                    )%numberOfDirections+numberOfDirections)%numberOfDirections
                
                );
                
            }
            
        }
        
        public static double getRotation(Vector3 position,Vector3 center) {
            
            return (position.Equals(center))?
                (0):
                ((Math.PI-Math.Atan2(position.X-center.X,position.Z-center.Z)+2*Math.PI)%(2*Math.PI));
            
        }
        
        public static double getRotationDifference(Vector3 position1,Vector3 position2,Vector3 center) {

            double rawDifference=(getRotation(position2,center)-getRotation(position1,center)+2*Math.PI)%(2*Math.PI);

            return (rawDifference<=Math.PI)?(rawDifference):(rawDifference-2*Math.PI);
            
        }
        
        public static Vector3 rotatePosition(Vector3 position,Vector3 center,double radian,bool preserveHeight=true) {

            Vector2 positionInVector2=new Vector2(position.X-center.X,position.Z-center.Z);
            double polarAngleAfterRotation=Math.PI-Math.Atan2(positionInVector2.X,positionInVector2.Y)+radian;
            
            return new Vector3((float)(center.X+Math.Sin(polarAngleAfterRotation)*positionInVector2.Length()),
                ((preserveHeight)?(position.Y):(center.Y)),
                (float)(center.Z-Math.Cos(polarAngleAfterRotation)*positionInVector2.Length()));
            
        }

        public static double convertPolarToCartesian(double polarRotation) {
            
            return Math.PI-polarRotation;
            
        }
        
        public static double convertDegreesToRadians(double degree) {
            
            return degree*Math.PI/180.0;
            
        }

        public static bool isLegalPartyIndex(int partyIndex) {

            return (0<=partyIndex&&partyIndex<=7);

        }
        
        public static bool isSupporter(int partyIndex) {

            return partyIndex switch {

                0 => true,
                1 => true,
                2 => true,
                3 => true,
                _ => false

            };

        }

        public static bool isDps(int partyIndex) {

            return partyIndex switch {

                4 => true,
                5 => true,
                6 => true,
                7 => true,
                _ => false

            };

        }
        
        public static bool isMelee(int partyIndex) {

            return partyIndex switch {

                0 => true,
                1 => true,
                4 => true,
                5 => true,
                _ => false

            };

        }
        
        public static bool isRanged(int partyIndex) {

            return partyIndex switch {

                2 => true,
                3 => true,
                6 => true,
                7 => true,
                _ => false

            };

        }

        public static bool isTank(int partyIndex) {
            
            return isSupporter(partyIndex)&&isMelee(partyIndex);
            
        }
        
        public static bool isHealer(int partyIndex) {
            
            return isSupporter(partyIndex)&&isRanged(partyIndex);
            
        }
        
        public static bool isMeleeDps(int partyIndex) {
            
            return isDps(partyIndex)&&isMelee(partyIndex);
            
        }
        
        public static bool isRangedDps(int partyIndex) {
            
            return isDps(partyIndex)&&isRanged(partyIndex);
            
        }

        public static bool isInGroup1(int partyIndex) {
            
            return partyIndex switch {

                0 => true,
                2 => true,
                4 => true,
                6 => true,
                _ => false

            };
            
        }
        
        public static bool isInGroup2(int partyIndex) {
            
            return partyIndex switch {

                1 => true,
                3 => true,
                5 => true,
                7 => true,
                _ => false

            };
            
        }
        
        #endregion
        
    }

    #region Extensions
    
    public static class EventExtensions
    {
        
        public static bool TryActionId(this Event @event,out uint actionId) {
            
            return TryParseUnsignedInteger(@event["ActionId"],out actionId);
            
        }
        
        public static bool TrySourceId(this Event @event,out ulong sourceId) {
            
            return Dancing_Mad_Ultimate.convertObjectIdToDecimal(@event["SourceId"],out sourceId);
            
        }
        
        public static bool TryTargetId(this Event @event,out ulong targetId) {
            
            return Dancing_Mad_Ultimate.convertObjectIdToDecimal(@event["TargetId"],out targetId);
            
        }
        
        public static bool TryIconId(this Event @event,out uint iconId) {
            
            return TryParseHexUnsignedInteger(@event["Id"],out iconId);
            
        }
        
        public static bool TrySourceRotation(this Event @event,out float rotation) {
            
            return TryParseFloat(@event["SourceRotation"],out rotation);
            
        }
        
        public static bool TrySourcePosition(this Event @event,out Vector3 position) {
            
            return TryParseVector3(@event["SourcePosition"],out position);
            
        }
        
        public static bool TryTargetPosition(this Event @event,out Vector3 position) {
            
            return TryParseVector3(@event["TargetPosition"],out position);
            
        }
        
        public static bool TryEffectPosition(this Event @event,out Vector3 position) {
            
            return TryParseVector3(@event["EffectPosition"],out position);
            
        }
        
        private static bool TryParseUnsignedInteger(string? raw,out uint result) {
            
            result=0;
            
            if(string.IsNullOrWhiteSpace(raw)) {
                
                return false;
                
            }
            
            string cleanString=raw.Trim();
            
            if(cleanString.StartsWith("0x",StringComparison.OrdinalIgnoreCase)) {
                
                return uint.TryParse(cleanString.Substring(2),System.Globalization.NumberStyles.HexNumber,null,out result);
                
            }
            
            try {
                
                result=JsonConvert.DeserializeObject<uint>(cleanString);
                return true;
                
            } catch {
                
                return uint.TryParse(cleanString,System.Globalization.NumberStyles.Integer,null,out result);
                
            }
            
        }
        
        private static bool TryParseHexUnsignedInteger(string? raw,out uint result) {
            
            result=0;
            
            if(string.IsNullOrWhiteSpace(raw)) {
                
                return false;
                
            }
            
            string cleanString=raw.Trim();
            cleanString=cleanString.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?cleanString.Substring(2):cleanString;
            
            return uint.TryParse(cleanString,System.Globalization.NumberStyles.HexNumber,null,out result);
            
        }
        
        private static bool TryParseFloat(string? raw,out float result) {
            
            result=0;
            
            if(string.IsNullOrWhiteSpace(raw)) {
                
                return false;
                
            }
            
            string cleanString=raw.Trim();
            
            try {
                
                result=JsonConvert.DeserializeObject<float>(cleanString);
                return true;
                
            } catch {
                
                return float.TryParse(cleanString,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out result);
                
            }
            
        }
        
        private static bool TryParseVector3(string? raw,out Vector3 result) {
            
            result=Vector3.Zero;
            
            if(string.IsNullOrWhiteSpace(raw)) {
                
                return false;
                
            }
            
            string cleanString=raw.Trim();
            
            try {
                
                result=JsonConvert.DeserializeObject<Vector3>(cleanString);
                return true;
                
            } catch {
                
            }
            
            if(!TryExtractFloat(cleanString,"X",out float x)||!TryExtractFloat(cleanString,"Y",out float y)||!TryExtractFloat(cleanString,"Z",out float z)) {
                
                return false;
                
            }
            
            result=new Vector3(x,y,z);
            return true;
            
        }
        
        private static bool TryExtractFloat(string raw,string key,out float result) {
            
            result=0;
            var match=Regex.Match(raw,$@"{key}\s*[:=]\s*(-?\d+(?:[\.,]\d+)?)",RegexOptions.IgnoreCase);
            
            if(!match.Success) {
                
                return false;
                
            }
            
            string value=match.Groups[1].Value.Replace(',','.');
            
            return float.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out result);
            
        }
        
    }
    
    public static class ScriptAccessoryExtensions
    {
        
        public static void tts(this ScriptAccessory accessory,string text,bool enableVanillaTts,bool enableDailyRoutinesTts) {
            
            if(enableVanillaTts) {
                    
                accessory.Method.TTS(text);
                    
            }

            else {
                
                if(enableDailyRoutinesTts) {
                    
                    accessory.Method.SendChat($"/pdr tts {text}");
                    
                }
                
            }
            
        }
        
        public static int getPlayerIndex(this ScriptAccessory accessory,ulong objectId) {
            
            return accessory.Data.PartyList.IndexOf((uint)objectId);
            
        }
        
        public static int getMyIndex(this ScriptAccessory accessory) {
            
            return accessory.Data.PartyList.IndexOf(accessory.Data.Me);
            
        }
        
        public static DrawPropertiesEdit waypointToPosition(this ScriptAccessory accessory,Vector3 targetPosition,int duration,int delay=0,string name="Waypoint",Vector4? colour=null) {
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();
            
            currentProperties.Name=name;
            currentProperties.Owner=accessory.Data.Me;
            currentProperties.TargetPosition=targetPosition;
            currentProperties.Color=colour??accessory.Data.DefaultSafeColor;
            currentProperties.Delay=delay;
            currentProperties.DestoryAt=duration;
            currentProperties.Scale=new Vector2(2);
            currentProperties.ScaleMode=ScaleMode.YByDistance;
            
            return currentProperties;
            
        }
        
        public static DrawPropertiesEdit waypointFromTo(this ScriptAccessory accessory,Vector3 sourcePosition,Vector3 targetPosition,int duration,int delay=0,string name="WaypointFromTo",Vector4? colour=null) {
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();
            
            currentProperties.Name=name;
            currentProperties.Owner=0;
            currentProperties.Position=sourcePosition;
            currentProperties.TargetPosition=targetPosition;
            currentProperties.Color=colour??accessory.Data.DefaultSafeColor;
            currentProperties.Delay=delay;
            currentProperties.DestoryAt=duration;
            currentProperties.Scale=new Vector2(2);
            currentProperties.ScaleMode=ScaleMode.YByDistance;
            
            return currentProperties;
            
        }
        
        public static DrawPropertiesEdit waypointToObject(this ScriptAccessory accessory,ulong targetObjectId,int duration,int delay=0,string name="WaypointToObject",Vector4? colour=null) {
            
            var currentProperties=accessory.Data.GetDefaultDrawProperties();
            
            currentProperties.Name=name;
            currentProperties.Owner=accessory.Data.Me;
            currentProperties.TargetObject=targetObjectId;
            currentProperties.Color=colour??accessory.Data.DefaultSafeColor;
            currentProperties.Delay=delay;
            currentProperties.DestoryAt=duration;
            currentProperties.Scale=new Vector2(2);
            currentProperties.ScaleMode=ScaleMode.YByDistance;
            
            return currentProperties;
            
        }
        
        public static bool TryGetObjectById(this ScriptAccessory accessory,ulong objectId,out KodakkuAssist.Data.IGameObject? gameObject) {
            
            gameObject=accessory.Data.Objects.SearchById(objectId);
            
            return gameObject!=null&&gameObject.IsValid();
            
        }
        
        public static bool TrySetObjectVisible(this ScriptAccessory accessory,ulong objectId,bool visible,out string message,int recoverInterval=0) {
            
            message=string.Empty;
            
            if(!accessory.TryGetObjectById(objectId,out var gameObject)||gameObject==null) {
                
                message=$"找不到对象 objectId=0x{objectId:X}。";
                return false;
                
            }
            
            return accessory.TrySetObjectVisible(gameObject,visible,out message,recoverInterval);
            
        }
        
        public static unsafe bool TrySetObjectVisible(this ScriptAccessory accessory,KodakkuAssist.Data.IGameObject? gameObject,bool visible,out string message,int recoverInterval=0) {
            
            message=string.Empty;
            
            if(gameObject==null||!gameObject.IsValid()||gameObject.Address==IntPtr.Zero) {
                
                message="对象为空或无效。";
                return false;
                
            }
            
            try {
                
                var clientObject=(ClientGameObject*)gameObject.Address;
                var oldFlags=clientObject->RenderFlags;
                var newFlags=visible?VisibilityFlags.None:VisibilityFlags.Model;
                
                clientObject->RenderFlags=newFlags;
                
                if(recoverInterval>0) {
                    
                    Task.Delay(recoverInterval).ContinueWith(_ => {
                        
                        try {
                            
                            if(!gameObject.IsValid()||gameObject.Address==IntPtr.Zero) {
                                
                                return;
                                
                            }
                            
                            var recoverObject=(ClientGameObject*)gameObject.Address;
                            
                            if(recoverObject->RenderFlags!=newFlags) {
                                
                                return;
                                
                            }
                            
                            recoverObject->RenderFlags=oldFlags;
                            
                        } catch(Exception exception) {
                            
                            accessory.Log.Error(exception.ToString());
                            
                        }
                        
                    });
                    
                }
                
                return true;
                
            } catch(Exception exception) {
                
                message=exception.ToString();
                accessory.Log.Error(message);
                return false;
                
            }
            
        }
        
        public static string getPlayerRoleName(this ScriptAccessory accessory,int partyIndex,bool fourPeople=false) {
            
            return partyIndex switch {
                
                0 => "MT",
                1 => fourPeople?"H1":"ST",
                2 => fourPeople?"D1":"H1",
                3 => fourPeople?"D2":"H2",
                4 => "D1",
                5 => "D2",
                6 => "D3",
                7 => "D4",
                _ => "unknown"
                
            };
            
        }
        
        public static string getPlayerRoleName(this ScriptAccessory accessory,ulong objectId,bool fourPeople=false) {
            
            return accessory.getPlayerRoleName(accessory.getPlayerIndex(objectId),fourPeople);
            
        }
        
    }
    
    #endregion
    
}
