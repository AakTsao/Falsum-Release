function roundSpeed(value) {
  return Math.round(value * 100) / 100;
}

function hasAtMostTwoDecimals(value) {
  return Math.abs(value * 100 - Math.round(value * 100)) <= 0.000000001;
}

falsum.registerTool({
  createForm(ctx) {
    const startTime = ctx.chart.playheadMs;
    const easingOptions = ctx.easing.names().map(function (name) {
      return {
        value: name,
        label: ctx.text("easing." + name)
      };
    });

    return {
      title: ctx.text("tool.name"),
      fields: [
        {
          key: "startTime",
          type: "time",
          label: ctx.text("form.startTime"),
          chartPick: true,
          required: true,
          defaultValue: startTime
        },
        {
          key: "startSpeed",
          type: "number",
          label: ctx.text("form.startSpeed"),
          step: 0.01,
          required: true,
          defaultValue: 1.0
        },
        {
          key: "endTime",
          type: "time",
          label: ctx.text("form.endTime"),
          chartPick: true,
          required: true,
          defaultValue: startTime + 1000
        },
        {
          key: "endSpeed",
          type: "number",
          label: ctx.text("form.endSpeed"),
          step: 0.01,
          required: true,
          defaultValue: 1.0
        },
        {
          key: "easing",
          type: "select",
          label: ctx.text("form.easing"),
          required: true,
          options: easingOptions,
          defaultValue: "linear"
        },
        {
          key: "nodes",
          type: "integer",
          label: ctx.text("form.nodes"),
          min: 2,
          max: 2000,
          required: true,
          defaultValue: 16
        }
      ]
    };
  },

  validate(values, ctx) {
    const errors = [];
    const startTime = values.startTime;
    const endTime = values.endTime;
    const nodes = values.nodes;

    if (!Number.isInteger(startTime) || startTime < 0) {
      errors.push({
        field: "startTime",
        message: ctx.text("validation.startTimeInvalid")
      });
    }
    if (!Number.isInteger(endTime) || endTime < 0) {
      errors.push({
        field: "endTime",
        message: ctx.text("validation.endTimeInvalid")
      });
    }
    if (endTime <= startTime) {
      errors.push({
        field: "endTime",
        message: ctx.text("validation.endAfterStart")
      });
    }
    if (!Number.isFinite(values.startSpeed) ||
        !hasAtMostTwoDecimals(values.startSpeed)) {
      errors.push({
        field: "startSpeed",
        message: ctx.text("validation.startSpeedPrecision")
      });
    }
    if (!Number.isFinite(values.endSpeed) ||
        !hasAtMostTwoDecimals(values.endSpeed)) {
      errors.push({
        field: "endSpeed",
        message: ctx.text("validation.endSpeedPrecision")
      });
    }
    if (!Number.isInteger(nodes) || nodes < 2 || nodes > 2000) {
      errors.push({
        field: "nodes",
        message: ctx.text("validation.nodesRange")
      });
    } else if (endTime > startTime && nodes > endTime - startTime + 1) {
      errors.push({
        field: "nodes",
        message: ctx.text("validation.nodesTooMany")
      });
    }
    if (ctx.easing.names().indexOf(values.easing) < 0) {
      errors.push({
        field: "easing",
        message: ctx.text("validation.easingInvalid")
      });
    }

    return errors;
  },

  run(ctx) {
    const startTime = ctx.inputs.startTime;
    const endTime = ctx.inputs.endTime;
    const startSpeed = roundSpeed(ctx.inputs.startSpeed);
    const endSpeed = roundSpeed(ctx.inputs.endSpeed);
    const easingName = ctx.inputs.easing;
    const nodes = ctx.inputs.nodes;

    const existing = ctx.chart.events.find({
      kind: "track",
      fromTimeMs: startTime,
      toTimeMs: endTime
    });
    for (let i = 0; i < existing.length; i += 1) {
      ctx.edit.events.remove(existing[i].key);
    }

    const generated = [];
    const seenTimes = new Set();
    for (let i = 0; i < nodes; i += 1) {
      const t = i / (nodes - 1);
      const timeMs = Math.round(startTime + (endTime - startTime) * t);
      if (seenTimes.has(timeMs)) {
        continue;
      }
      seenTimes.add(timeMs);

      const eased = ctx.easing.evaluate(easingName, t);
      generated.push({
        kind: "track",
        timeMs: timeMs,
        multiplier: roundSpeed(
          startSpeed + (endSpeed - startSpeed) * eased
        )
      });
    }

    ctx.edit.events.addMany(generated);
    ctx.ui.notify(ctx.text("notify.generated", {
      count: generated.length
    }));
    return {
      message: ctx.text("result.complete"),
      details: {
        replaced: existing.length,
        generated: generated.length
      }
    };
  }
});

