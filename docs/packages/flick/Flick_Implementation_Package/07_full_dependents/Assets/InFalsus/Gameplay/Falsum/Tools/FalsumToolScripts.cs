namespace InFalsus.Gameplay.Falsum.Tools
{
    internal static class FalsumToolScripts
    {
        public const string RuntimeBootstrap = @"
globalThis.falsum = Object.freeze({
  registerTool: function (definition) {
    __falsumRegister(definition);
  }
});

globalThis.__falsumEasing = (function () {
  function clamp(t) {
    return Math.max(0, Math.min(1, Number(t) || 0));
  }
  function linear(t) { return t; }
  function sineIn(t) { return 1 - Math.cos((t * Math.PI) / 2); }
  function sineOut(t) { return Math.sin((t * Math.PI) / 2); }
  function sineInOut(t) { return -(Math.cos(Math.PI * t) - 1) / 2; }
  function quadIn(t) { return t * t; }
  function quadOut(t) { return 1 - (1 - t) * (1 - t); }
  function quadInOut(t) {
    return t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2;
  }
  function cubicIn(t) { return t * t * t; }
  function cubicOut(t) { return 1 - Math.pow(1 - t, 3); }
  function cubicInOut(t) {
    return t < 0.5
      ? 4 * t * t * t
      : 1 - Math.pow(-2 * t + 2, 3) / 2;
  }
  function quartIn(t) { return t * t * t * t; }
  function quartOut(t) { return 1 - Math.pow(1 - t, 4); }
  function quartInOut(t) {
    return t < 0.5
      ? 8 * t * t * t * t
      : 1 - Math.pow(-2 * t + 2, 4) / 2;
  }
  function quintIn(t) { return t * t * t * t * t; }
  function quintOut(t) { return 1 - Math.pow(1 - t, 5); }
  function quintInOut(t) {
    return t < 0.5
      ? 16 * t * t * t * t * t
      : 1 - Math.pow(-2 * t + 2, 5) / 2;
  }
  function expoIn(t) {
    return t === 0 ? 0 : Math.pow(2, 10 * t - 10);
  }
  function expoOut(t) {
    return t === 1 ? 1 : 1 - Math.pow(2, -10 * t);
  }
  function expoInOut(t) {
    if (t === 0) return 0;
    if (t === 1) return 1;
    return t < 0.5
      ? Math.pow(2, 20 * t - 10) / 2
      : (2 - Math.pow(2, -20 * t + 10)) / 2;
  }
  function circIn(t) {
    return 1 - Math.sqrt(1 - Math.min(1, t * t));
  }
  function circOut(t) {
    return Math.sqrt(1 - Math.pow(t - 1, 2));
  }
  function circInOut(t) {
    return t < 0.5
      ? (1 - Math.sqrt(1 - Math.pow(2 * t, 2))) / 2
      : (Math.sqrt(1 - Math.pow(-2 * t + 2, 2)) + 1) / 2;
  }
  var c1 = 1.70158;
  var c3 = c1 + 1;
  function backIn(t) {
    return c3 * t * t * t - c1 * t * t;
  }
  function backOut(t) {
    return 1 + c3 * Math.pow(t - 1, 3) +
      c1 * Math.pow(t - 1, 2);
  }
  function backInOut(t) {
    var c2 = c1 * 1.525;
    return t < 0.5
      ? (Math.pow(2 * t, 2) * ((c2 + 1) * 2 * t - c2)) / 2
      : (Math.pow(2 * t - 2, 2) *
        ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2;
  }
  var c4 = (2 * Math.PI) / 3;
  function elasticIn(t) {
    if (t === 0) return 0;
    if (t === 1) return 1;
    return -Math.pow(2, 10 * t - 10) *
      Math.sin((t * 10 - 10.75) * c4);
  }
  function elasticOut(t) {
    if (t === 0) return 0;
    if (t === 1) return 1;
    return Math.pow(2, -10 * t) *
      Math.sin((t * 10 - 0.75) * c4) + 1;
  }
  function elasticInOut(t) {
    if (t === 0) return 0;
    if (t === 1) return 1;
    var c5 = (2 * Math.PI) / 4.5;
    return t < 0.5
      ? -(Math.pow(2, 20 * t - 10) *
        Math.sin((20 * t - 11.125) * c5)) / 2
      : (Math.pow(2, -20 * t + 10) *
        Math.sin((20 * t - 11.125) * c5)) / 2 + 1;
  }
  function bounceOut(t) {
    var n1 = 7.5625;
    var d1 = 2.75;
    if (t < 1 / d1) return n1 * t * t;
    if (t < 2 / d1) {
      t -= 1.5 / d1;
      return n1 * t * t + 0.75;
    }
    if (t < 2.5 / d1) {
      t -= 2.25 / d1;
      return n1 * t * t + 0.9375;
    }
    t -= 2.625 / d1;
    return n1 * t * t + 0.984375;
  }
  function bounceIn(t) { return 1 - bounceOut(1 - t); }
  function bounceInOut(t) {
    return t < 0.5
      ? (1 - bounceOut(1 - 2 * t)) / 2
      : (1 + bounceOut(2 * t - 1)) / 2;
  }

  var functions = {
    linear: linear,
    sineIn: sineIn, sineOut: sineOut, sineInOut: sineInOut,
    quadIn: quadIn, quadOut: quadOut, quadInOut: quadInOut,
    cubicIn: cubicIn, cubicOut: cubicOut, cubicInOut: cubicInOut,
    quartIn: quartIn, quartOut: quartOut, quartInOut: quartInOut,
    quintIn: quintIn, quintOut: quintOut, quintInOut: quintInOut,
    expoIn: expoIn, expoOut: expoOut, expoInOut: expoInOut,
    circIn: circIn, circOut: circOut, circInOut: circInOut,
    backIn: backIn, backOut: backOut, backInOut: backInOut,
    elasticIn: elasticIn, elasticOut: elasticOut,
    elasticInOut: elasticInOut,
    bounceIn: bounceIn, bounceOut: bounceOut,
    bounceInOut: bounceInOut
  };
  var names = [
    'linear',
    'sineIn', 'sineOut', 'sineInOut',
    'quadIn', 'quadOut', 'quadInOut',
    'cubicIn', 'cubicOut', 'cubicInOut',
    'quartIn', 'quartOut', 'quartInOut',
    'quintIn', 'quintOut', 'quintInOut',
    'expoIn', 'expoOut', 'expoInOut',
    'circIn', 'circOut', 'circInOut',
    'backIn', 'backOut', 'backInOut',
    'elasticIn', 'elasticOut', 'elasticInOut',
    'bounceIn', 'bounceOut', 'bounceInOut'
  ];
  return Object.freeze({
    names: function () { return names.slice(); },
    evaluate: function (name, t) {
      var fn = functions[name];
      if (typeof fn !== 'function') {
        throw new Error('Unknown easing: ' + name);
      }
      return fn(clamp(t));
    }
  });
})();

function __falsumMatches(item, query) {
  if (!query) return true;
  if (query.kind != null && item.kind !== query.kind) return false;
  if (query.fromTimeMs != null && item.timeMs < query.fromTimeMs) {
    return false;
  }
  if (query.toTimeMs != null && item.timeMs > query.toTimeMs) {
    return false;
  }
  return true;
}

function __falsumFind(items, query) {
  return items.filter(function (item) {
    return __falsumMatches(item, query);
  });
}

function __falsumTokenValue(value) {
  return value && typeof value === 'object' ? value.token : value;
}

var __falsumBase64Alphabet =
  'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';

function __falsumBase64ToBytes(text) {
  var output = [];
  var buffer = 0;
  var bits = 0;
  for (var i = 0; i < text.length; i += 1) {
    var c = text.charAt(i);
    if (c === '=') break;
    var value = __falsumBase64Alphabet.indexOf(c);
    if (value < 0) continue;
    buffer = (buffer << 6) | value;
    bits += 6;
    if (bits >= 8) {
      bits -= 8;
      output.push((buffer >> bits) & 255);
    }
  }
  return new Uint8Array(output);
}

function __falsumBytesToBase64(bytes) {
  var output = '';
  var buffer = 0;
  var bits = 0;
  for (var i = 0; i < bytes.length; i += 1) {
    buffer = (buffer << 8) | (bytes[i] & 255);
    bits += 8;
    while (bits >= 6) {
      bits -= 6;
      output += __falsumBase64Alphabet.charAt((buffer >> bits) & 63);
    }
  }
  if (bits > 0) {
    output += __falsumBase64Alphabet.charAt((buffer << (6 - bits)) & 63);
  }
  while (output.length % 4 !== 0) output += '=';
  return output;
}

globalThis.__falsumBuildContext = function (includeInputs, allowEdits) {
  var data = __falsumContextData;
  var ctx = {
    apiVersion: 1,
    tool: data.tool,
    locale: data.locale,
    text: function (key, variables) {
      var template = data.texts[String(key)];
      if (template == null) return String(key);
      if (!variables) return template;
      return template.replace(
        /\{([A-Za-z_][A-Za-z0-9_]*)\}/g,
        function (match, name) {
          return Object.prototype.hasOwnProperty.call(
            variables,
            name
          )
            ? String(variables[name])
            : match;
        }
      );
    },
    chart: {
      playheadMs: data.chart.playheadMs,
      summary: function () { return data.chart.summary; },
      events: {
        find: function (query) {
          return __falsumFind(data.chart.events, query);
        }
      },
      notes: {
        find: function (query) {
          return __falsumFind(data.chart.notes, query);
        }
      },
      skyArea: {
        findDisconnectedIntervals: function () {
          return JSON.parse(
            __hostSkyAreaDisconnectedIntervals()
          );
        }
      }
    },
    easing: __falsumEasing,
    ui: {
      notify: function (message) {
        __hostNotify(String(message));
      }
    },
    log: {
      debug: function () { __hostLog('debug', Array.prototype.join.call(arguments, ' ')); },
      info: function () { __hostLog('info', Array.prototype.join.call(arguments, ' ')); },
      warn: function () { __hostLog('warn', Array.prototype.join.call(arguments, ' ')); },
      error: function () { __hostLog('error', Array.prototype.join.call(arguments, ' ')); }
    },
    files: {
      toolAsset: function (path) {
        return { token: __hostFileToken('asset', String(path)) };
      },
      toolData: function (path) {
        return { token: __hostFileToken('data', String(path)) };
      },
      readText: function (token) {
        return __hostFileReadText(__falsumTokenValue(token));
      },
      readBytes: function (token) {
        return __falsumBase64ToBytes(
          __hostFileReadBase64(__falsumTokenValue(token))
        );
      },
      writeText: function (token, text) {
        __hostFileWriteText(
          __falsumTokenValue(token),
          String(text)
        );
      },
      writeBytes: function (token, bytes) {
        __hostFileWriteBase64(
          __falsumTokenValue(token),
          __falsumBytesToBase64(bytes)
        );
      }
    }
  };

  if (includeInputs) {
    ctx.inputs = __falsumInputsData;
  }
  if (allowEdits) {
    ctx.edit = {
      events: {
        add: function (value) { __hostEventAdd(JSON.stringify(value)); },
        addMany: function (values) {
          for (var i = 0; i < values.length; i += 1) {
            __hostEventAdd(JSON.stringify(values[i]));
          }
        },
        update: function (key, patch) {
          __hostEventUpdate(String(key), JSON.stringify(patch));
        },
        remove: function (key) {
          __hostEventRemove(String(key));
        }
      },
      notes: {
        add: function (value) { __hostNoteAdd(JSON.stringify(value)); },
        addMany: function (values) {
          for (var i = 0; i < values.length; i += 1) {
            __hostNoteAdd(JSON.stringify(values[i]));
          }
        },
        update: function (key, patch) {
          __hostNoteUpdate(String(key), JSON.stringify(patch));
        },
        remove: function (key) {
          __hostNoteRemove(String(key));
        }
      },
      setTiming: function (value) {
        __hostSetTiming(JSON.stringify(value));
      }
    };
  }
  return ctx;
};
";
    }
}

