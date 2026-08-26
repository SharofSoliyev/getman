namespace GetMan.Services;

/// <summary>
/// The JavaScript prelude injected into every script sandbox. Implements the chai-flavoured
/// assertion library plus the pm.* surface on top of the C# bridge exposed as __host.
/// </summary>
internal static class ScriptBootstrap
{
    public const string Source = """
var __tests = {};

function __typeOf(v) {
  if (v === null) return 'null';
  if (v === undefined) return 'undefined';
  if (Array.isArray(v)) return 'array';
  return typeof v;
}

function __show(v) {
  try {
    if (typeof v === 'string') return '"' + v + '"';
    if (v === undefined) return 'undefined';
    if (v === null) return 'null';
    if (typeof v === 'object') return JSON.stringify(v);
    return String(v);
  } catch (e) { return String(v); }
}

function __deepEqual(a, b) {
  if (a === b) return true;
  if (typeof a !== typeof b) return false;
  if (a === null || b === null) return false;
  if (typeof a !== 'object') return a === b;
  if (Array.isArray(a) !== Array.isArray(b)) return false;
  if (Array.isArray(a)) {
    if (a.length !== b.length) return false;
    for (var i = 0; i < a.length; i++) if (!__deepEqual(a[i], b[i])) return false;
    return true;
  }
  var ka = Object.keys(a), kb = Object.keys(b);
  if (ka.length !== kb.length) return false;
  for (var j = 0; j < ka.length; j++) {
    if (kb.indexOf(ka[j]) < 0) return false;
    if (!__deepEqual(a[ka[j]], b[ka[j]])) return false;
  }
  return true;
}

function AssertionError(message) {
  var e = new Error(message);
  e.name = 'AssertionError';
  return e;
}

function Assertion(obj, flags) {
  this._obj = obj;
  this._negate = flags && flags.negate ? true : false;
  this._deep = flags && flags.deep ? true : false;
  this._any = flags && flags.any ? true : false;
  this._isResponse = flags && flags.isResponse ? true : false;
  this._length = flags && flags.length ? true : false;
}

Assertion.prototype._clone = function (over) {
  var f = { negate: this._negate, deep: this._deep, any: this._any, isResponse: this._isResponse, length: this._length };
  if (over) for (var k in over) f[k] = over[k];
  return new Assertion(this._obj, f);
};

Assertion.prototype._assert = function (ok, msg, negMsg) {
  var pass = this._negate ? !ok : ok;
  if (!pass) throw AssertionError(this._negate ? (negMsg || ('expected NOT ' + msg)) : msg);
  return this;
};

(function () {
  var chains = ['to', 'be', 'been', 'is', 'that', 'which', 'and', 'has', 'have', 'with', 'at', 'of', 'same', 'but', 'does', 'still', 'also', 'contains'];
  for (var i = 0; i < chains.length; i++) {
    (function (name) {
      Object.defineProperty(Assertion.prototype, name, {
        get: function () { return this; },
        configurable: true
      });
    })(chains[i]);
  }

  Object.defineProperty(Assertion.prototype, 'not', {
    get: function () { this._negate = !this._negate; return this; }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'deep', {
    get: function () { this._deep = true; return this; }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'any', {
    get: function () { this._any = true; return this; }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'all', {
    get: function () { this._any = false; return this; }, configurable: true
  });

  Object.defineProperty(Assertion.prototype, 'ok', {
    get: function () {
      if (this._isResponse) {
        var c = this._obj.code;
        return this._assert(c >= 200 && c < 300, 'expected response to have a 2xx status but got ' + c);
      }
      return this._assert(!!this._obj, 'expected ' + __show(this._obj) + ' to be truthy');
    }, configurable: true
  });

  Object.defineProperty(Assertion.prototype, 'true', {
    get: function () { return this._assert(this._obj === true, 'expected ' + __show(this._obj) + ' to be true'); }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'false', {
    get: function () { return this._assert(this._obj === false, 'expected ' + __show(this._obj) + ' to be false'); }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'null', {
    get: function () { return this._assert(this._obj === null, 'expected ' + __show(this._obj) + ' to be null'); }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'undefined', {
    get: function () { return this._assert(this._obj === undefined, 'expected ' + __show(this._obj) + ' to be undefined'); }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'exist', {
    get: function () { return this._assert(this._obj !== null && this._obj !== undefined, 'expected ' + __show(this._obj) + ' to exist'); }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'empty', {
    get: function () {
      var o = this._obj, isEmpty;
      if (o === null || o === undefined) isEmpty = true;
      else if (typeof o === 'string' || Array.isArray(o)) isEmpty = o.length === 0;
      else if (typeof o === 'object') isEmpty = Object.keys(o).length === 0;
      else isEmpty = false;
      return this._assert(isEmpty, 'expected ' + __show(this._obj) + ' to be empty');
    }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'json', {
    get: function () {
      if (this._isResponse) {
        var ct = this._obj.headers.get('content-type') || '';
        return this._assert(ct.toLowerCase().indexOf('json') >= 0, 'expected response content-type to be json but got "' + ct + '"');
      }
      var okj = true;
      try { JSON.parse(typeof this._obj === 'string' ? this._obj : JSON.stringify(this._obj)); } catch (e) { okj = false; }
      return this._assert(okj, 'expected value to be valid json');
    }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'success', {
    get: function () { var c = this._obj.code; return this._assert(c >= 200 && c < 300, 'expected a success status but got ' + c); }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'redirection', {
    get: function () { var c = this._obj.code; return this._assert(c >= 300 && c < 400, 'expected a redirection status but got ' + c); }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'clientError', {
    get: function () { var c = this._obj.code; return this._assert(c >= 400 && c < 500, 'expected a client error status but got ' + c); }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'serverError', {
    get: function () { var c = this._obj.code; return this._assert(c >= 500 && c < 600, 'expected a server error status but got ' + c); }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'error', {
    get: function () { var c = this._obj.code; return this._assert(c >= 400, 'expected an error status but got ' + c); }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'accepted', {
    get: function () { return this._assert(this._obj.code === 202, 'expected 202 but got ' + this._obj.code); }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'withBody', {
    get: function () { return this._assert(!!this._obj.text() , 'expected response to have a body'); }, configurable: true
  });
  Object.defineProperty(Assertion.prototype, 'lengthOf', {
    get: function () {
      var self = this;
      var fn = function (n) { return self._lengthCheck(n); };
      fn.above = function (n) { return self._assert(self._len() > n, 'expected length ' + self._len() + ' to be above ' + n); };
      fn.below = function (n) { return self._assert(self._len() < n, 'expected length ' + self._len() + ' to be below ' + n); };
      fn.least = function (n) { return self._assert(self._len() >= n, 'expected length ' + self._len() + ' to be at least ' + n); };
      fn.most = function (n) { return self._assert(self._len() <= n, 'expected length ' + self._len() + ' to be at most ' + n); };
      return fn;
    }, configurable: true
  });
})();

Assertion.prototype._len = function () {
  var o = this._obj;
  if (o === null || o === undefined) return -1;
  if (typeof o === 'string' || Array.isArray(o)) return o.length;
  if (typeof o === 'object') return Object.keys(o).length;
  return -1;
};

Assertion.prototype._lengthCheck = function (n) {
  return this._assert(this._len() === n, 'expected length ' + this._len() + ' to equal ' + n);
};

Assertion.prototype.equal = function (v) {
  if (this._deep) return this.eql(v);
  return this._assert(this._obj === v, 'expected ' + __show(this._obj) + ' to equal ' + __show(v),
    'expected ' + __show(this._obj) + ' not to equal ' + __show(v));
};
Assertion.prototype.equals = Assertion.prototype.equal;
Assertion.prototype.eq = Assertion.prototype.equal;

Assertion.prototype.eql = function (v) {
  return this._assert(__deepEqual(this._obj, v), 'expected ' + __show(this._obj) + ' to deeply equal ' + __show(v),
    'expected ' + __show(this._obj) + ' not to deeply equal ' + __show(v));
};
Assertion.prototype.eqls = Assertion.prototype.eql;

Assertion.prototype.a = function (type) {
  return this._assert(__typeOf(this._obj) === String(type).toLowerCase(),
    'expected ' + __show(this._obj) + ' to be a ' + type + ' but it is a ' + __typeOf(this._obj));
};
Assertion.prototype.an = Assertion.prototype.a;

Assertion.prototype.above = function (n) { return this._assert(this._obj > n, 'expected ' + __show(this._obj) + ' to be above ' + n); };
Assertion.prototype.greaterThan = Assertion.prototype.above;
Assertion.prototype.gt = Assertion.prototype.above;
Assertion.prototype.below = function (n) { return this._assert(this._obj < n, 'expected ' + __show(this._obj) + ' to be below ' + n); };
Assertion.prototype.lessThan = Assertion.prototype.below;
Assertion.prototype.lt = Assertion.prototype.below;
Assertion.prototype.least = function (n) { return this._assert(this._obj >= n, 'expected ' + __show(this._obj) + ' to be at least ' + n); };
Assertion.prototype.gte = Assertion.prototype.least;
Assertion.prototype.most = function (n) { return this._assert(this._obj <= n, 'expected ' + __show(this._obj) + ' to be at most ' + n); };
Assertion.prototype.lte = Assertion.prototype.most;
Assertion.prototype.within = function (lo, hi) {
  return this._assert(this._obj >= lo && this._obj <= hi, 'expected ' + __show(this._obj) + ' to be within ' + lo + '..' + hi);
};
Assertion.prototype.closeTo = function (v, delta) {
  return this._assert(Math.abs(this._obj - v) <= delta, 'expected ' + __show(this._obj) + ' to be close to ' + v);
};

Assertion.prototype.match = function (re) {
  return this._assert(re.test(String(this._obj)), 'expected ' + __show(this._obj) + ' to match ' + re);
};
Assertion.prototype.matches = Assertion.prototype.match;

Assertion.prototype.include = function (v) {
  var o = this._obj, ok = false;
  if (typeof o === 'string') ok = o.indexOf(v) >= 0;
  else if (Array.isArray(o)) {
    if (this._deep) { for (var i = 0; i < o.length; i++) if (__deepEqual(o[i], v)) { ok = true; break; } }
    else ok = o.indexOf(v) >= 0;
  } else if (o && typeof o === 'object' && v && typeof v === 'object') {
    ok = true;
    for (var k in v) if (!__deepEqual(o[k], v[k])) { ok = false; break; }
  }
  return this._assert(ok, 'expected ' + __show(this._obj) + ' to include ' + __show(v));
};
Assertion.prototype.includes = Assertion.prototype.include;
Assertion.prototype.contain = Assertion.prototype.include;
Assertion.prototype.contains = Assertion.prototype.include;

Assertion.prototype.oneOf = function (list) {
  var o = this._obj, ok = false;
  for (var i = 0; i < list.length; i++) if (__deepEqual(list[i], o)) { ok = true; break; }
  return this._assert(ok, 'expected ' + __show(o) + ' to be one of ' + __show(list));
};

Assertion.prototype.property = function (name, value) {
  var o = this._obj;
  var path = String(name).split('.');
  var cur = o, found = true;
  for (var i = 0; i < path.length; i++) {
    if (cur === null || cur === undefined || !(path[i] in Object(cur))) { found = false; break; }
    cur = cur[path[i]];
  }
  if (arguments.length < 2) {
    this._assert(found, 'expected ' + __show(o) + ' to have property "' + name + '"');
    return found ? new Assertion(cur, { negate: false }) : this;
  }
  this._assert(found && (this._deep ? __deepEqual(cur, value) : cur === value),
    'expected property "' + name + '" to equal ' + __show(value) + ' but got ' + __show(cur));
  return new Assertion(cur, { negate: false });
};
Assertion.prototype.ownProperty = Assertion.prototype.property;

Assertion.prototype.keys = function () {
  var wanted = Array.isArray(arguments[0]) ? arguments[0] : Array.prototype.slice.call(arguments);
  var have = Object.keys(this._obj || {});
  var ok = true;
  for (var i = 0; i < wanted.length; i++) if (have.indexOf(wanted[i]) < 0) { ok = false; break; }
  if (!this._any && ok) ok = have.length === wanted.length || this._negate;
  return this._assert(ok, 'expected ' + __show(have) + ' to have keys ' + __show(wanted));
};
Assertion.prototype.key = Assertion.prototype.keys;

Assertion.prototype.members = function (list) {
  var o = this._obj || [];
  var ok = true;
  for (var i = 0; i < list.length; i++) {
    var found = false;
    for (var j = 0; j < o.length; j++) if (__deepEqual(o[j], list[i])) { found = true; break; }
    if (!found) { ok = false; break; }
  }
  return this._assert(ok, 'expected ' + __show(o) + ' to have members ' + __show(list));
};

Assertion.prototype.length = function (n) { return this._lengthCheck(n); };
Assertion.prototype.throw = function () {
  var threw = false;
  try { this._obj(); } catch (e) { threw = true; }
  return this._assert(threw, 'expected function to throw');
};

/* response specific assertions */
Assertion.prototype.status = function (code) {
  var actual = this._obj.code;
  if (typeof code === 'string') {
    return this._assert(String(this._obj.status).toLowerCase() === code.toLowerCase(),
      'expected status "' + this._obj.status + '" to be "' + code + '"');
  }
  return this._assert(actual === code, 'expected response code to be ' + code + ' but got ' + actual);
};

Assertion.prototype.header = function (name, value) {
  var v = this._obj.headers.get(name);
  if (arguments.length < 2)
    return this._assert(v !== null && v !== undefined, 'expected response to have header "' + name + '"');
  return this._assert(v === value, 'expected header "' + name + '" to be ' + __show(value) + ' but got ' + __show(v));
};

Assertion.prototype.body = function (expected) {
  var text = this._obj.text();
  if (arguments.length === 0) return this._assert(!!text, 'expected response to have a body');
  if (expected instanceof RegExp) return this._assert(expected.test(text), 'expected body to match ' + expected);
  if (typeof expected === 'object') return this._assert(__deepEqual(this._obj.json(), expected), 'expected body to deeply equal ' + __show(expected));
  return this._assert(text === expected, 'expected body to equal ' + __show(expected));
};

Assertion.prototype.jsonBody = function (a, b) {
  var j;
  try { j = this._obj.json(); } catch (e) { return this._assert(false, 'expected response body to be valid json'); }
  if (arguments.length === 0) return this._assert(j !== undefined && j !== null, 'expected a json body');
  if (arguments.length === 1) {
    if (typeof a === 'object') return this._assert(__deepEqual(j, a), 'expected json body to deeply equal ' + __show(a));
    return new Assertion(j, {}).property(a);
  }
  return new Assertion(j, { deep: true }).property(a, b);
};

Assertion.prototype.responseTime = function () { return new Assertion(this._obj.responseTime, {}); };

function expect(obj) { return new Assertion(obj, {}); }
expect.fail = function (msg) { throw AssertionError(msg || 'expect.fail()'); };

function assert(value, message) {
  if (!value) throw AssertionError(message || 'assertion failed');
}
assert.equal = function (a, b, m) { if (a != b) throw AssertionError(m || (__show(a) + ' != ' + __show(b))); };
assert.strictEqual = function (a, b, m) { if (a !== b) throw AssertionError(m || (__show(a) + ' !== ' + __show(b))); };
assert.deepEqual = function (a, b, m) { if (!__deepEqual(a, b)) throw AssertionError(m || 'objects are not deeply equal'); };
assert.notEqual = function (a, b, m) { if (a == b) throw AssertionError(m || 'values are equal'); };
assert.isTrue = function (a, m) { if (a !== true) throw AssertionError(m || 'not true'); };
assert.isFalse = function (a, m) { if (a !== false) throw AssertionError(m || 'not false'); };
assert.isOk = function (a, m) { if (!a) throw AssertionError(m || 'not ok'); };
assert.fail = function (m) { throw AssertionError(m || 'assert.fail()'); };

/* ------------------------------------------------------------------ pm */

function __VarScope(kind) { this._kind = kind; }
__VarScope.prototype.get = function (k) { var v = __host.varGet(this._kind, String(k)); return v === null ? undefined : v; };
__VarScope.prototype.set = function (k, v) { __host.varSet(this._kind, String(k), v === undefined || v === null ? '' : (typeof v === 'object' ? JSON.stringify(v) : String(v))); };
__VarScope.prototype.has = function (k) { return __host.varHas(this._kind, String(k)); };
__VarScope.prototype.unset = function (k) { __host.varUnset(this._kind, String(k)); };
__VarScope.prototype.clear = function () { __host.varClear(this._kind); };
__VarScope.prototype.toObject = function () { return JSON.parse(__host.varToObject(this._kind)); };
__VarScope.prototype.replaceIn = function (s) { return __host.replaceIn(String(s)); };
__VarScope.prototype.toJSON = function () { return this.toObject(); };

function __Headers(list) {
  this._list = list || [];
}
__Headers.prototype.get = function (name) {
  var n = String(name).toLowerCase();
  for (var i = 0; i < this._list.length; i++) if (String(this._list[i].key).toLowerCase() === n) return this._list[i].value;
  return null;
};
__Headers.prototype.has = function (name) { return this.get(name) !== null; };
__Headers.prototype.all = function () { return this._list.slice(); };
__Headers.prototype.add = function (h) {
  if (typeof h === 'string') { var p = h.split(':'); this._list.push({ key: p[0].trim(), value: p.slice(1).join(':').trim() }); }
  else this._list.push({ key: h.key, value: h.value });
};
__Headers.prototype.upsert = function (h) {
  var key = typeof h === 'string' ? h.split(':')[0].trim() : h.key;
  var val = typeof h === 'string' ? h.split(':').slice(1).join(':').trim() : h.value;
  var n = String(key).toLowerCase();
  for (var i = 0; i < this._list.length; i++) {
    if (String(this._list[i].key).toLowerCase() === n) { this._list[i].value = val; return; }
  }
  this._list.push({ key: key, value: val });
};
__Headers.prototype.remove = function (name) {
  var n = String(name).toLowerCase();
  this._list = this._list.filter(function (x) { return String(x.key).toLowerCase() !== n; });
};
__Headers.prototype.each = function (fn) { for (var i = 0; i < this._list.length; i++) fn(this._list[i]); };
__Headers.prototype.toObject = function () {
  var o = {};
  for (var i = 0; i < this._list.length; i++) o[this._list[i].key] = this._list[i].value;
  return o;
};
__Headers.prototype.count = function () { return this._list.length; };

function __makeUrl(raw) {
  var u = {
    _raw: raw,
    toString: function () { return this._raw; },
    getHost: function () { try { return this._raw.split('//')[1].split('/')[0].split('?')[0]; } catch (e) { return ''; } },
    getPath: function () { try { var r = this._raw.split('//')[1]; var i = r.indexOf('/'); var p = i < 0 ? '/' : r.substring(i); return p.split('?')[0]; } catch (e) { return '/'; } },
    getQueryString: function () { var i = this._raw.indexOf('?'); return i < 0 ? '' : this._raw.substring(i + 1); }
  };
  Object.defineProperty(u, 'query', {
    get: function () {
      var self = this;
      return {
        get: function (k) {
          var qs = self.getQueryString();
          if (!qs) return null;
          var parts = qs.split('&');
          for (var i = 0; i < parts.length; i++) {
            var eq = parts[i].indexOf('=');
            var key = eq < 0 ? parts[i] : parts[i].substring(0, eq);
            if (decodeURIComponent(key) === k) return eq < 0 ? '' : decodeURIComponent(parts[i].substring(eq + 1));
          }
          return null;
        },
        all: function () {
          var qs = self.getQueryString(), out = [];
          if (!qs) return out;
          qs.split('&').forEach(function (p) {
            var eq = p.indexOf('=');
            out.push({ key: eq < 0 ? p : p.substring(0, eq), value: eq < 0 ? '' : p.substring(eq + 1) });
          });
          return out;
        }
      };
    }, configurable: true
  });
  return u;
}

var pm = (function () {
  var api = {};

  api.environment = new __VarScope('environment');
  api.globals = new __VarScope('globals');
  api.collectionVariables = new __VarScope('collection');
  api.variables = new __VarScope('any');
  api.iterationData = new __VarScope('data');
  api.vault = new __VarScope('globals');

  api.info = JSON.parse(__host.infoJson());

  var reqRaw = JSON.parse(__host.requestJson());
  var request = {
    method: reqRaw.method,
    headers: new __Headers(reqRaw.headers),
    body: reqRaw.body,
    auth: reqRaw.auth
  };
  request.url = __makeUrl(reqRaw.url);
  request.toJSON = function () {
    return { method: this.method, url: this.url.toString(), headers: this.headers.all(), body: this.body };
  };
  api.request = request;

  api.__flushRequest = function () {
    __host.applyRequest(JSON.stringify({
      method: request.method,
      url: request.url.toString(),
      headers: request.headers.all(),
      body: request.body
    }));
  };

  if (__host.hasResponse()) {
    var resRaw = JSON.parse(__host.responseJson());
    var response = {
      code: resRaw.code,
      status: resRaw.status,
      responseTime: resRaw.responseTime,
      responseSize: resRaw.responseSize,
      headers: new __Headers(resRaw.headers),
      _text: resRaw.body,
      text: function () { return this._text; },
      json: function () { return JSON.parse(this._text); },
      stream: function () { return this._text; },
      reason: function () { return this.status; },
      toJSON: function () { return { code: this.code, status: this.status, body: this._text, headers: this.headers.all() }; }
    };
    Object.defineProperty(response, 'to', {
      get: function () { return new Assertion(this, { isResponse: true }); }, configurable: true
    });
    api.response = response;
  }

  api.cookies = {
    get: function (name) { var v = __host.cookieGet(String(name)); return v === null ? undefined : v; },
    has: function (name) { return __host.cookieGet(String(name)) !== null; },
    toObject: function () { return JSON.parse(__host.cookiesJson()); },
    jar: function () {
      return {
        get: function (url, name, cb) { var v = __host.cookieGet(String(name)); if (cb) cb(null, v); return v; },
        set: function (url, name, value, cb) { if (cb) cb(null); },
        clear: function (url, cb) { if (cb) cb(null); }
      };
    }
  };

  api.expect = expect;
  api.assert = assert;

  api.test = function (name, fn) {
    var started = __host.now();
    try {
      if (fn && fn.length > 0) {
        var doneCalled = false;
        var doneErr = null;
        fn(function (err) { doneCalled = true; doneErr = err || null; });
        if (doneErr) throw doneErr;
      } else if (fn) {
        fn();
      }
      __host.addTest(String(name), true, '', __host.now() - started);
    } catch (e) {
      __host.addTest(String(name), false, (e && e.message) ? e.message : String(e), __host.now() - started);
    }
    return api;
  };
  api.test.skip = function (name) { __host.skipTest(String(name)); return api; };

  api.execution = {
    setNextRequest: function (n) { __host.setNextRequest(n === null ? '' : String(n)); },
    skipRequest: function () { __host.skipRequest(); },
    location: { current: api.info.requestName }
  };
  api.setNextRequest = api.execution.setNextRequest;

  api.sendRequest = function (options, callback) {
    var payload = (typeof options === 'string') ? { url: options, method: 'GET' } : options;
    var raw;
    try {
      raw = __host.sendRequest(JSON.stringify(payload));
    } catch (e) {
      if (callback) callback(e, null);
      return;
    }
    var parsed = JSON.parse(raw);
    if (parsed.error) { if (callback) callback(new Error(parsed.error), null); return; }
    var res = {
      code: parsed.code,
      status: parsed.status,
      responseTime: parsed.responseTime,
      responseSize: parsed.responseSize,
      headers: new __Headers(parsed.headers),
      _text: parsed.body,
      text: function () { return this._text; },
      json: function () { return JSON.parse(this._text); }
    };
    Object.defineProperty(res, 'to', { get: function () { return new Assertion(this, { isResponse: true }); }, configurable: true });
    if (callback) callback(null, res);
    return res;
  };

  api.require = function (name) { throw new Error('require("' + name + '") is not available in GetMan scripts'); };

  return api;
})();

/* ------------------------------------------------- legacy postman shims */

var postman = {
  setEnvironmentVariable: function (k, v) { pm.environment.set(k, v); },
  getEnvironmentVariable: function (k) { return pm.environment.get(k); },
  clearEnvironmentVariable: function (k) { pm.environment.unset(k); },
  clearEnvironmentVariables: function () { pm.environment.clear(); },
  setGlobalVariable: function (k, v) { pm.globals.set(k, v); },
  getGlobalVariable: function (k) { return pm.globals.get(k); },
  clearGlobalVariable: function (k) { pm.globals.unset(k); },
  clearGlobalVariables: function () { pm.globals.clear(); },
  setNextRequest: function (n) { pm.execution.setNextRequest(n); },
  getResponseHeader: function (n) { return pm.response ? pm.response.headers.get(n) : null; },
  getResponseCookie: function (n) { var v = pm.cookies.get(n); return v ? { name: n, value: v } : null; }
};

var tests = new Proxy({}, {
  set: function (target, key, value) {
    target[key] = value;
    __host.addTest(String(key), !!value, value ? '' : 'expected a truthy value', 0);
    return true;
  }
});

var responseCode = pm.response ? { code: pm.response.code, name: pm.response.status } : null;
var responseBody = pm.response ? pm.response.text() : '';
var responseTime = pm.response ? pm.response.responseTime : 0;
var environment = pm.environment.toObject();
var globals = pm.globals.toObject();
var data = pm.iterationData.toObject();

var console = {
  log: function () { __host.log('log', __join(arguments)); },
  info: function () { __host.log('info', __join(arguments)); },
  warn: function () { __host.log('warn', __join(arguments)); },
  error: function () { __host.log('error', __join(arguments)); },
  debug: function () { __host.log('debug', __join(arguments)); },
  table: function () { __host.log('log', __join(arguments)); },
  clear: function () { }
};

function __join(args) {
  var parts = [];
  for (var i = 0; i < args.length; i++) {
    var a = args[i];
    if (typeof a === 'object' && a !== null) {
      try { parts.push(JSON.stringify(a, null, 2)); } catch (e) { parts.push(String(a)); }
    } else parts.push(String(a));
  }
  return parts.join(' ');
}

function btoa(s) { return __host.base64Encode(String(s)); }
function atob(s) { return __host.base64Decode(String(s)); }
function xml2Json(s) { return JSON.parse(__host.xmlToJson(String(s))); }
function setTimeout(fn) { if (typeof fn === 'function') fn(); return 0; }
function clearTimeout() { }
function require(name) { return pm.require(name); }
""";
}
