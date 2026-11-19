function noteEndTime(note) {
  return note.kind === "hold"
    ? note.endTimeMs
    : note.timeMs;
}

function noteLaneEnd(note) {
  return note.startLane + Math.max(1, note.widthLanes);
}

function lanesOverlap(left, right) {
  return left.startLane < noteLaneEnd(right) &&
    right.startLane < noteLaneEnd(left);
}

function timesOverlap(left, right) {
  return left.timeMs <= noteEndTime(right) &&
    right.timeMs <= noteEndTime(left);
}

function formatTime(timeMs, ctx) {
  return ctx.text("report.time", {
    value: String(Math.max(0, Math.round(timeMs)))
  });
}

function formatLanes(note) {
  const start = note.startLane;
  const end = noteLaneEnd(note) - 1;
  return start === end ? "[" + start + "]" : "[" + start + "-" + end + "]";
}

function noteName(note, ctx) {
  return ctx.text(
    note.kind === "hold" ? "note.hold" : "note.tap"
  );
}

function describeNote(note, ctx) {
  let description = ctx.text("report.noteDescription", {
    kind: noteName(note, ctx),
    id: note.id,
    lanes: formatLanes(note),
    time: formatTime(note.timeMs, ctx)
  });
  if (note.kind === "hold") {
    description = ctx.text("report.holdDescription", {
      kind: noteName(note, ctx),
      id: note.id,
      lanes: formatLanes(note),
      start: formatTime(note.timeMs, ctx),
      end: formatTime(note.endTimeMs, ctx)
    });
  }
  return description;
}

function classify(left, right, ctx) {
  if (left.kind === "tap" && right.kind === "tap") {
    return ctx.text("report.overlap.tapTap");
  }
  if (left.kind === "hold" && right.kind === "hold") {
    if (left.timeMs === right.timeMs) {
      return ctx.text("report.overlap.time");
    }
    if (left.timeMs === right.endTimeMs ||
        right.timeMs === left.endTimeMs) {
      return ctx.text("report.overlap.time");
    }
    if ((left.timeMs <= right.timeMs &&
         left.endTimeMs >= right.endTimeMs) ||
        (right.timeMs <= left.timeMs &&
         right.endTimeMs >= left.endTimeMs)) {
      return ctx.text("report.overlap.nestedHold");
    }
    return ctx.text("report.overlap.holdRange");
  }

  const hold = left.kind === "hold" ? left : right;
  const tap = left.kind === "tap" ? left : right;
  if (tap.timeMs === hold.timeMs) {
    return ctx.text("report.overlap.time");
  }
  if (tap.timeMs === hold.endTimeMs) {
    return ctx.text("report.overlap.time");
  }
  if (tap.timeMs > hold.timeMs &&
      tap.timeMs < hold.endTimeMs) {
    return ctx.text("report.overlap.nestedTap");
  }
  return ctx.text("report.overlap.tapHoldRange");
}

falsum.registerTool({
  createForm(ctx) {
    return {
      title: ctx.text("tool.name"),
      fields: []
    };
  },

  validate(values, ctx) {
    return [];
  },

  run(ctx) {
    const notes = ctx.chart.notes.find({ kind: "tap" })
      .concat(ctx.chart.notes.find({ kind: "hold" }));

    notes.sort(function (left, right) {
      if (left.timeMs !== right.timeMs) {
        return left.timeMs - right.timeMs;
      }
      return left.id - right.id;
    });

    const issues = [];
    for (let i = 0; i < notes.length; i += 1) {
      const left = notes[i];
      const leftEnd = noteEndTime(left);
      for (let j = i + 1; j < notes.length; j += 1) {
        const right = notes[j];
        if (right.timeMs > leftEnd) {
          break;
        }
        if (!lanesOverlap(left, right) ||
            !timesOverlap(left, right)) {
          continue;
        }
        const overlapStart = Math.max(left.timeMs, right.timeMs);
        issues.push({
          title: classify(left, right, ctx),
          message: ctx.text("report.overlapMessage", {
            left: describeNote(left, ctx),
            right: describeNote(right, ctx)
          }),
          severity: "error",
          timeMs: overlapStart,
          noteKeys: [left.key, right.key]
        });
      }
    }

    issues.sort(function (left, right) {
      if (left.timeMs !== right.timeMs) {
        return left.timeMs - right.timeMs;
      }
      return left.title.localeCompare(right.title);
    });

    const summary = ctx.text("report.summary", {
      notes: notes.length,
      issues: issues.length
    });
    return {
      message: issues.length === 0
        ? ctx.text("report.passed")
        : ctx.text("report.foundIssues", {
            issues: issues.length
          }),
      details: {
        scannedTapHold: notes.length,
        tapHoldIssues: issues.length,
        issues: issues.length
      },
      report: {
        title: ctx.text("tool.name"),
        summary: summary,
        emptyText: ctx.text("report.emptyText"),
        items: issues
      }
    };
  }
});
