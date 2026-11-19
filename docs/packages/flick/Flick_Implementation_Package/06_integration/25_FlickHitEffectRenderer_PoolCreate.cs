// ==== 片段：左右 burst 池的创建（各 4 个） ====
// 源文件: Assets/InFalsus/Gameplay/Presentation/Effects/FlickHitEffectRenderer.cs
// 行范围: 96-131 （共 36 行；本片段为逐字摘录，未改代码）
// 说明  : 模板分别来自 binding.FlickBurstRightTemplate / LeftTemplate。
// ---------------------------------------------------------------------------


            _engine = engine;
            _binding = binding;
            _camera = Camera.main;
            _burstMaterial = CreateBurstMaterial();

            BuildBurstPool(
                _rightBursts,
                binding.FlickBurstRightTemplate,
                "Right");
            BuildBurstPool(
                _leftBursts,
                binding.FlickBurstLeftTemplate,
                "Left");
            BuildFeedbackPool(
                exactPlusTexture,
                exactTexture,
                nearTexture,
                breakTexture,
                feedbackMaterial);

            _engine.Judged += HandleJudged;
            _initialized = true;
        }

        public void Tick(double nowMs, bool paused)
        {
            if (!_initialized)
            {
                return;
            }

            ActiveBurstCount = TickBursts(_rightBursts, nowMs, paused);
            ActiveBurstCount += TickBursts(_leftBursts, nowMs, paused);
            ActiveFeedbackCount = TickFeedbacks(nowMs);
        }
