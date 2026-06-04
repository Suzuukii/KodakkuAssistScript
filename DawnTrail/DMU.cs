using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Text.RegularExpressions;
using KodakkuAssist.Module.GameEvent;
using KodakkuAssist.Module.Draw;
using KodakkuAssist.Module.Draw.Manager;
using KodakkuAssist.Module.GameOperate;
using KodakkuAssist.Script;
using Newtonsoft.Json;
using Dalamud.Utility.Numerics;
using KodakkuAssist.Data;
using ClientGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace SuzuukiiKodakkuAssist
{

    [ScriptType(name:"妖星乱舞绝境战",
        territorys:[1363],
        guid:"42683c02-0c71-4fd6-b49a-19163b24b22c",
        version:"0.0.0.22",
        note:scriptNotes,
        author:"Suzuukii")]

    public class Dancing_Mad_Ultimate
    {
        
        public const string scriptNotes=
            """
            妖星乱舞绝境战的脚本。
            等主流打法稳定后，会将成熟指路方法融入主脚本。

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
        #endregion
        
        #region Variables_And_Semaphores
        
        private volatile int majorPhase=1;
        private volatile int phase=1;
        private readonly object developer_loggedActionIdsLock=new object();
        private HashSet<uint> developer_loggedActionIds=new HashSet<uint>();
        
        private volatile int phase1_pulseCannonDrawn=0;
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
        private static readonly Vector3 PHASE1_PULSE_CANNON_SOURCE_POSITION=new Vector3(100,0,65);
        private const int PHASE1_PULSE_CANNON_DELAY=5625;
        private const int PHASE1_PULSE_CANNON_DURATION=4375;
        private const int PHASE1_FLAGRANT_FIRE_DRAW_DURATION=5875;
        private const int PHASE1_EXPANDING_FREEZE_DURATION=5000;
        private const int PHASE1_THRUMMING_THUNDER_DURATION=5000;
        private const int PHASE1_ACTION_EFFECT_HIDE_DURATION=5000;
        private const int PHASE1_ACTION_EFFECT_HIDE_RECOVERY_DELAY=125;
        private const int PHASE1_ACTION_EFFECT_HIDE_TOTAL_DURATION=PHASE1_ACTION_EFFECT_HIDE_DURATION+PHASE1_ACTION_EFFECT_HIDE_RECOVERY_DELAY;
        private const int PHASE1_EXPLOSION_DRAW_DURATION=3000;
        private const int PHASE1_CHAIN_TRAP_RADIUS=6;
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
            phase1_gravenImage2IsFirstHalf=true;
            resetPhase1ObfuscationData();
            resetPhase1FlagrantFireIconData();
            resetPhase1FlagrantFireHeadMarkerPairData();
            
        }
        
        private void resetPhase1ObfuscationData() {
            
            phase1_isFlagrantFireFake=false;
            phase1_isExpandingFreezeFake=false;
            phase1_isThrummingThunderFake=false;
            phase1_flagrantFireTruthConfirmed.Reset();
            
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
