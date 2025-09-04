using Core.Debug.FastString;
using Game.Core.Debug.FastString;
using System;
using UnityEngine;
using Game.Features.Production.Machines;
using Game.Data;
using Game.Data.Definition.Recipes;

namespace Game.Features.Production.Processing
{
    public class ProductionTickService : MonoBehaviour
    {
        [SerializeField] private float tickInterval = 0.2f;
        [SerializeField] private MachineRuntimeManager machineManager;

        private float _accum;
        private MachineProcessService processService;

        private void Awake()
        {
            if (!machineManager)
                machineManager = FindFirstObjectByType<MachineRuntimeManager>();
            processService = new MachineProcessService();
        }

        private void Update()
        {
            if (machineManager == null || processService == null)
            {
                return;
            }
            _accum += Time.deltaTime;
            if (_accum < tickInterval) return;

            int safety = 0; // 补偿长帧，避免时间漂移
            while (_accum >= tickInterval && safety < 8)
            {
                StepOnce(tickInterval);
                _accum -= tickInterval;
                safety++;
            }
            if (_accum > tickInterval * 4f)
                _accum = 0f;
        }

        /// <summary>
        /// 单步执行
        /// </summary>
        private void StepOnce(float dt)
        {
            var machines = machineManager.Machines;
            foreach (var m in machines)
            {
                try
                {
                    processService.Advance(m, dt, continuousProduction: true);
                }
                catch (Exception e)
                {
                    FastString.Acquire().Tag("ProductionTickService")
                        .T("[ProductionTick] Error ! 机器实例").I(m.InstanceId)
                        .T("出错, 请检查配方/输入输出状态. 异常信息: ").T(e.ToString())
                        .Log();
                }
            }
        }
        
        public MachineProcessService MachineProcessService => processService;
        
    }
}
