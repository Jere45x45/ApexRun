/**
 * SubirMarca — Cloud Code (JavaScript) de ApexRun.
 *
 * El juego ya no escribe en la tabla de la contrarreloj ni en Cloud Save: le
 * manda la vuelta a este script, que la revisa y, si está bien, la guarda con
 * permisos de servidor (serviceToken). Access Control (Assets/Servicios/Acceso)
 * no deja que los jugadores escriban directo.
 *
 * Qué se revisa (todo con la vuelta grabada, el fantasma; el tiempo que manda
 * el juego no se usa: la marca es el tiempo del fantasma):
 *  - El fantasma: Base64 -> GZip -> GhostRecording v1, con límites de tamaño.
 *  - Los tiempos: empieza en 0, nunca va para atrás, sin huecos de más de 0,25 s,
 *    y la vuelta dura lo que dice.
 *  - La largada: parado, en la grilla (cerca de la llegada y del lado de atrás).
 *  - El recorrido: pasa por los 25 checkpoints en el orden de la pista (al
 *    derecho o al revés), y termina al cruzar la llegada.
 *  - La velocidad: nunca más que lo que da el motor y las ruedas declarados al
 *    corte de rpm (con margen); sin saltos ni aceleraciones imposibles.
 *  - El agarre en las curvas: la aceleración de costado (percentil 99) no puede
 *    pasar lo que dan las gomas y el alerón declarados (con 20 % de margen). Si
 *    alguien acelera una vuelta real, en las curvas haría falta más agarre.
 *  - Que no sea la copia de la vuelta de otro piloto (los fantasmas son públicos).
 *  - Los datos: tabla conocida, clima coherente con la tabla (seco o lluvia),
 *    nombre limpio, piezas con formato válido. La fecha la pone el servidor.
 *  - Que mejore la marca del jugador en esa tabla.
 *  - Límite de envíos por jugador (se cuenta en un dato protegido de Cloud Save,
 *    que el jugador puede leer pero no escribir).
 *
 * MANTENIMIENTO: los checkpoints y los motores/ruedas de abajo (entre <datos> y
 * </datos>) salen de la escena Race y de las piezas. Si cambia la pista o se
 * agrega un motor o ruedas, actualizarlos con el menú del editor
 * "ApexRun > Servicios > Actualizar datos de SubirMarca" y volver a desplegar.
 */

const axios = require("axios-1.6");

// <datos> (generado por ServerCheckDataExport.cs el 11/11/2026; no editar a mano)
const GATES = [
  {"i":0,"c":[-76.423,2.166,-713.201],"r":[-0.4583,0,-0.8888],"u":[0,1,0],"f":[0.8888,0,-0.4583],"h":[7.75,2.5,0.5]},
  {"i":1,"c":[-35.139,2.293,-738.264],"r":[-0.1812,0,-0.9834],"u":[0,1,0],"f":[0.9834,0,-0.1812],"h":[7.75,2.5,0.5]},
  {"i":2,"c":[9.856,2.501,-758.039],"r":[-0.8744,0,-0.4852],"u":[0,1,0],"f":[0.4852,0,-0.8744],"h":[7.75,2.5,0.5]},
  {"i":3,"c":[-7.418,2.774,-800.766],"r":[-0.6207,0,0.784],"u":[0,1,0],"f":[-0.784,0,-0.6207],"h":[7.75,2.5,0.5]},
  {"i":4,"c":[-36.066,3.24,-840.429],"r":[-0.6856,0,0.728],"u":[0,1,0],"f":[-0.728,0,-0.6856],"h":[7.75,2.5,0.5]},
  {"i":5,"c":[-82.115,2.62,-837.35],"r":[0.8311,0,0.5561],"u":[0,1,0],"f":[-0.5561,0,0.8311],"h":[7.75,2.5,0.5]},
  {"i":6,"c":[-56.999,2.204,-801.538],"r":[0.4125,0,-0.911],"u":[0,1,0],"f":[0.911,0,0.4125],"h":[7.75,2.5,0.5]},
  {"i":7,"c":[-41.208,1.967,-766.849],"r":[0.5515,0,0.8342],"u":[0,1,0],"f":[-0.8342,0,0.5515],"h":[7.75,2.5,0.5]},
  {"i":8,"c":[-89.323,1.981,-751.464],"r":[0.2557,0,0.9668],"u":[0,1,0],"f":[-0.9668,0,0.2557],"h":[7.75,2.5,0.5]},
  {"i":9,"c":[-138.032,2.368,-737.567],"r":[0.1413,0,0.99],"u":[0,1,0],"f":[-0.99,0,0.1413],"h":[7.75,2.5,0.5]},
  {"i":10,"c":[-181.248,2.489,-757.773],"r":[-0.8406,0,0.5416],"u":[0,1,0],"f":[-0.5416,0,-0.8406],"h":[7.75,2.5,0.5]},
  {"i":11,"c":[-157.822,2.449,-785.105],"r":[0.3111,0,-0.9504],"u":[0,1,0],"f":[0.9504,0,0.3111],"h":[7.875,2.5,0.5]},
  {"i":12,"c":[-112.529,2.233,-773.76],"r":[-0.7914,0,-0.6113],"u":[0,1,0],"f":[0.6113,0,-0.7914],"h":[7.75,2.5,0.5]},
  {"i":13,"c":[-125.366,2.38,-816.197],"r":[-0.4994,0,0.8664],"u":[0,1,0],"f":[-0.8664,0,-0.4994],"h":[7.75,2.5,0.5]},
  {"i":14,"c":[-172.22,2.064,-835.408],"r":[-0.4059,0,0.9139],"u":[0,1,0],"f":[-0.9139,0,-0.4059],"h":[7.75,2.5,0.5]},
  {"i":15,"c":[-219.329,2.299,-838.422],"r":[0.7588,0,0.6513],"u":[0,1,0],"f":[-0.6513,0,0.7588],"h":[7.75,2.5,0.5]},
  {"i":16,"c":[-262.291,2.548,-824.407],"r":[-0.2762,0,0.9611],"u":[0,1,0],"f":[-0.9611,0,-0.2762],"h":[7.75,2.5,0.5]},
  {"i":17,"c":[-310.281,3.578,-840.239],"r":[-0.1244,0,0.9922],"u":[0,1,0],"f":[-0.9922,0,-0.1244],"h":[7.75,2.5,0.5]},
  {"i":18,"c":[-344.738,3.596,-812.499],"r":[0.9986,0,-0.0528],"u":[0,1,0],"f":[0.0528,0,0.9986],"h":[7.75,2.5,0.5]},
  {"i":19,"c":[-322.984,2.658,-767.63],"r":[0.7381,0,-0.6746],"u":[0,1,0],"f":[0.6746,0,0.7381],"h":[7.75,2.5,0.5]},
  {"i":20,"c":[-277.86,2.441,-768.584],"r":[-0.4981,0,-0.8671],"u":[0,1,0],"f":[0.8671,0,-0.4981],"h":[7.75,2.5,0.5]},
  {"i":21,"c":[-252.165,2.661,-740.841],"r":[0.9971,0,0.0756],"u":[0,1,0],"f":[-0.0756,0,0.9971],"h":[7.75,2.5,0.5]},
  {"i":22,"c":[-228.063,3.105,-702.084],"r":[0.1045,0,-0.9945],"u":[0,1,0],"f":[0.9945,0,0.1045],"h":[10.625,2.5,0.5]},
  {"i":23,"c":[-177.102,2.916,-705.488],"r":[-0.0353,0,-0.9994],"u":[0,1,0],"f":[0.9994,0,-0.0353],"h":[7.75,2.5,0.5]},
  {"i":24,"c":[-126.5,2.451,-708.039],"r":[0,0,-1],"u":[0,1,0],"f":[1,0,0],"h":[7.875,2.5,0.5]}
];

// Velocidad del eje al corte de rpm (rad/s) de cada motor: maxRpm * 2π / 60 / gearRatio.
const ENGINES = {
  "engine_auto": 189.077, "engine_electric": 203.028, "engine_lancha": 221.075, "engine_lavarropas": 193.329, "engine_moto": 238.699
};

// Radio de las ruedas (m).
const WHEELS = {
  "wheels_overboard": 0.175, "wheels_bici": 0.185, "wheels_honda-mean-mower": 0.18, "wheels_slick": 0.165
};

// Agarre de costado de cada goma (TireSettings.lateralFriction).
const WHEEL_GRIP = {
  "wheels_overboard": 1.9, "wheels_bici": 1.2, "wheels_honda-mean-mower": 1.4, "wheels_slick": 1.7
};

// Superficie de carga aerodinámica de cada kit (AeroSettings.downforceArea, m²).
const AERO_AREA = {
  "kitaero_basic": 0.35, "kitaero_pro": 0.04, "kitaero_carton": 0.08, "kitaero_kartcars": 0.3, "kitaero_timeattack": 0.65
};
// </datos>

// Aceleración de costado medida (percentil 99, m/s²; ver lateralP99) en las vueltas reales
// de la tabla, con gomas slick (agarre 1,7) y kit timeattack (0,65 m²): de 17,0 a 18,0.
// Las otras combinaciones se escalan con el agarre y la carga aerodinámica.
const GRIP_REFERENCE = { lateral: 18.0, grip: 1.7, area: 0.65 };
// Cuánto agarre suma la carga aerodinámica a la velocidad típica de curva (18 m/s) para
// un kart de ~160 kg: 0,5 × 1,225 × v² / peso, por m².
const DOWNFORCE_PER_AREA = 0.5 * 1.225 * 18 * 18 / (160 * 9.81);

const BOARDS = {
  "tt-race": { reversed: false, wet: false },
  "tt-race-r": { reversed: true, wet: false },
  "tt-race-wet": { reversed: false, wet: true },
  "tt-race-r-wet": { reversed: true, wet: true },
  "tt-prueba": { reversed: false, wet: false } // TEMPORAL: solo para probar el guardado (11/11/2026)
};

const LIMITS = {
  maxBase64: 300000,            // ~4 veces un fantasma de una vuelta
  maxSamples: 6000,             // 4 minutos a 25 muestras por segundo
  minLap: 20, maxLap: 600,      // segundos
  maxGap: 0.25,                 // s entre muestras
  lapTolerance: 0.25,           // s entre el tiempo de la vuelta y la última muestra
  startDistance: 40,            // m de la llegada al largar
  startSpeed: 5,                // m/s promedio en el primer medio segundo
  speedMargin: 1.10, speedExtra: 1.0,   // tope = velocidad al corte × 1,10 + 1 m/s
  maxAcceleration: 20,          // m/s², ventanas de 0,5 s (lo real anda por 9,5)
  lateralMargin: 1.20,          // agarre en curvas: 20 % más que lo esperado para esas piezas
  gateMargin: [2.0, 1.5, 3.0],  // m de más en cada checkpoint (costado, alto, espesor)
  finishWindow: 0.5,            // s: la llegada se cruza al final de la vuelta
  copyDistance: 1.5,            // m promedio: menos que esto con la vuelta de otro es copia
  copyTimeRange: 0.05,          // se compara con vueltas de ±5 % de tiempo
  copyCandidates: 10,
  sendsWindow: 600, maxSends: 30, minSendGap: 3   // envíos: 30 cada 10 min, uno cada 3 s
};

const WET_THRESHOLD = 0.25;
const WEATHER_RAIN = 2, WEATHER_STORM = 3;

module.exports = async ({ params, context, logger }) => {
  try {
    return await submit(params, context, logger);
  } catch (error) {
    logger.error("SubirMarca: error interno", { "error.message": error && error.message });
    return { ok: false, codigo: "servidor" };
  }
};

module.exports.params = {
  tabla: { type: "String", required: true },
  fantasma: { type: "String", required: true },
  nombre: { type: "String", required: false },
  motor: { type: "String", required: true },
  ruedas: { type: "String", required: true },
  aero: { type: "String", required: false },
  volante: { type: "String", required: false },
  clima: { type: "Numeric", required: true },
  hora: { type: "Numeric", required: true },
  humedad: { type: "Numeric", required: true }
};

// ---------------------------------------------------------------------------
// Envío
// ---------------------------------------------------------------------------

async function submit(params, context, logger) {
  const { projectId, playerId } = context;
  const api = services(context);

  // 1. Datos básicos (sin tocar nada todavía).
  const input = readInput(params);
  if (input.error) return reject(logger, input.error, input.detail);

  // 2. Límite de envíos (se cuenta antes de revisar: así no se puede probar a ciegas).
  const limit = await countSend(api, projectId, playerId, "tt_envios", LIMITS.maxSends);
  if (limit) return reject(logger, limit);

  // 3. La vuelta.
  let ghost;
  try {
    ghost = parseGhost(gunzip(decodeBase64(input.ghostText, LIMITS.maxBase64), 12 + 32 * LIMITS.maxSamples), LIMITS.maxSamples);
  } catch (error) {
    return reject(logger, "datos", error.message);
  }

  const check = validateLap(ghost, input.board, input.engine, input.wheels, input.grip, input.area);
  if (check) return reject(logger, check.code, check.detail);

  const score = Math.round(ghost.lapTime * 1000) / 1000;

  // 4. ¿Mejora su marca?
  const mine = await api.getScore(projectId, input.boardId, playerId);
  if (mine && mine.score <= score) return reject(logger, "no-mejora", null, { mejor: mine.score });

  // 5. ¿Es la copia de la vuelta de otro?
  const copy = await findCopy(api, projectId, input.boardId, playerId, ghost, score);
  if (copy) return reject(logger, "copia", copy);

  // 6. Guardar: primero el fantasma (y el nombre), después la marca.
  const writes = [api.setPublic(projectId, playerId, ghostKey(input.boardId), input.ghostText)];
  if (input.name) writes.push(api.setPublic(projectId, playerId, "tt_nombre", input.name));
  await Promise.all(writes);

  const metadata = {
    v: 2,
    clima: input.weather, hora: input.time, humedad: input.wetness,
    nombre: input.name,
    motor: input.engineId, ruedas: input.wheelsId, aero: input.aero, volante: input.steering,
    fecha: Math.floor(Date.now() / 1000)
  };

  const entry = await api.addScore(projectId, input.boardId, playerId, score, metadata);
  logger.info("SubirMarca: aceptada", { tabla: input.boardId, tiempo: score });

  return { ok: true, codigo: "ok", tiempo: score, puesto: entry && typeof entry.rank === "number" ? entry.rank + 1 : 0 };
}

function reject(logger, code, detail, extra) {
  logger.info("SubirMarca: rechazada", { codigo: code, detalle: detail ? String(detail).slice(0, 200) : "" });
  return Object.assign({ ok: false, codigo: code }, extra || {});
}

function ghostKey(boardId) {
  return "ghost_" + boardId.replace(/-/g, "_");
}

// ---------------------------------------------------------------------------
// Datos del juego
// ---------------------------------------------------------------------------

const PART_ID = /^[a-z0-9_-]{1,40}$/;

function readInput(params) {
  const boardId = params.tabla;
  if (typeof boardId !== "string" || !Object.prototype.hasOwnProperty.call(BOARDS, boardId)) return { error: "tabla" };
  const board = BOARDS[boardId];

  const ghostText = params.fantasma;
  if (typeof ghostText !== "string" || ghostText.length === 0 || ghostText.length > LIMITS.maxBase64) return { error: "datos", detail: "tamaño" };

  const engineId = String(params.motor || "");
  const wheelsId = String(params.ruedas || "");
  const aero = String(params.aero || "");
  const steering = String(params.volante || "");
  if (!PART_ID.test(engineId) || !PART_ID.test(wheelsId)) return { error: "piezas" };
  if (aero && !PART_ID.test(aero)) return { error: "piezas" };
  if (steering && !PART_ID.test(steering)) return { error: "piezas" };

  // Piezas que no se conocen (por ejemplo, nuevas): se usa la combinación más rápida.
  const engine = Object.prototype.hasOwnProperty.call(ENGINES, engineId) ? ENGINES[engineId] : Math.max(...Object.values(ENGINES));
  const wheels = Object.prototype.hasOwnProperty.call(WHEELS, wheelsId) ? WHEELS[wheelsId] : Math.max(...Object.values(WHEELS));
  const grip = Object.prototype.hasOwnProperty.call(WHEEL_GRIP, wheelsId) ? WHEEL_GRIP[wheelsId] : Math.max(...Object.values(WHEEL_GRIP));
  const area = Object.prototype.hasOwnProperty.call(AERO_AREA, aero) ? AERO_AREA[aero] : Math.max(...Object.values(AERO_AREA));

  const weather = Number(params.clima);
  const time = Number(params.hora);
  const wetness = Number(params.humedad);
  if (!Number.isInteger(weather) || weather < 0 || weather > 5) return { error: "clima" };
  if (!Number.isInteger(time) || time < 0 || time > 3) return { error: "clima" };
  if (!Number.isFinite(wetness) || wetness < 0 || wetness > 1) return { error: "clima" };

  // La tabla de lluvia es para vueltas con la pista mojada, y la seca para las secas.
  const wet = wetness > WET_THRESHOLD || weather === WEATHER_RAIN || weather === WEATHER_STORM;
  if (wet !== board.wet) return { error: "clima" };

  return {
    boardId, board, ghostText, engineId, wheelsId, aero, steering,
    engine, wheels, grip, area,
    weather, time, wetness: Math.round(wetness * 100) / 100,
    name: cleanName(params.nombre)
  };
}

/** Igual que TimeTrialRecords.CleanName en el juego: sin < > ni saltos, espacios simples, hasta 16. */
function cleanName(value) {
  if (typeof value !== "string") return "";
  let name = value.replace(/[\u0000-\u001f\u007f<>]/g, " ").replace(/\s+/g, " ").trim();
  if (name.length > 16) name = name.slice(0, 16).trim();
  return name;
}

// ---------------------------------------------------------------------------
// Revisión de la vuelta
// ---------------------------------------------------------------------------

function validateLap(g, board, engineAxle, wheelRadius, grip, area) {
  const n = g.count;

  // Tiempos.
  if (g.lapTime < LIMITS.minLap || g.lapTime > LIMITS.maxLap) return { code: "tiempo", detail: "duración " + g.lapTime };
  if (g.t[0] < 0 || g.t[0] > 0.1) return { code: "tiempo", detail: "inicio " + g.t[0] };
  for (let i = 1; i < n; i++) {
    const dt = g.t[i] - g.t[i - 1];
    if (dt < 0 || dt > LIMITS.maxGap) return { code: "tiempo", detail: "hueco " + dt + " en " + i };
  }
  if (Math.abs(g.lapTime - g.t[n - 1]) > LIMITS.lapTolerance) return { code: "tiempo", detail: "fin " + g.t[n - 1] + " vs " + g.lapTime };

  // Rotaciones (cuaterniones unitarios).
  for (let i = 0; i < n; i++) {
    const len = Math.hypot(g.qx[i], g.qy[i], g.qz[i], g.qw[i]);
    if (len < 0.9 || len > 1.1) return { code: "datos", detail: "rotación " + i };
  }

  // Velocidad.
  const cap = engineAxle * wheelRadius * LIMITS.speedMargin + LIMITS.speedExtra;

  for (let i = 1; i < n; i++) {
    const dt = g.t[i] - g.t[i - 1];
    const d = Math.hypot(g.x[i] - g.x[i - 1], g.y[i] - g.y[i - 1], g.z[i] - g.z[i - 1]);
    if (d > cap * dt * 2 + 0.75) return { code: "velocidad", detail: "salto de " + d.toFixed(2) + " m en " + i };
  }

  const speeds = windowSpeeds(g, 0.2);
  for (const s of speeds) {
    if (s.v > cap) return { code: "velocidad", detail: s.v.toFixed(1) + " m/s (tope " + cap.toFixed(1) + ") en " + s.t.toFixed(1) };
  }

  for (let i = 0, j = 0; i < speeds.length; i++) {
    while (j < speeds.length - 1 && speeds[j].t - speeds[i].t < 0.5) j++;
    const dt = speeds[j].t - speeds[i].t;
    if (dt < 0.4) continue;
    const a = (speeds[j].v - speeds[i].v) / dt;
    if (a > LIMITS.maxAcceleration) return { code: "velocidad", detail: "aceleración " + a.toFixed(1) + " en " + speeds[i].t.toFixed(1) };
  }

  // Agarre en las curvas.
  const allowed = lateralAllowed(grip, area);
  const lateral = lateralP99(g);
  if (lateral > allowed) return { code: "agarre", detail: lateral.toFixed(1) + " m/s² (tope " + allowed.toFixed(1) + ")" };

  // Largada: parado, en la grilla (cerca de la llegada, del lado de atrás).
  const finish = GATES[GATES.length - 1];
  const dx = g.x[0] - finish.c[0], dz = g.z[0] - finish.c[2];
  if (Math.hypot(dx, dz) > LIMITS.startDistance) return { code: "largada", detail: "lejos de la grilla" };
  const along = dx * finish.f[0] + dz * finish.f[2];
  if (board.reversed ? along < -2 : along > 2) return { code: "largada", detail: "del lado equivocado" };
  if (speeds.length > 0) {
    let sum = 0, count = 0;
    for (const s of speeds) { if (s.t > 0.5) break; sum += s.v; count++; }
    if (count > 0 && sum / count > LIMITS.startSpeed) return { code: "largada", detail: "lanzada" };
  }

  // Recorrido: los checkpoints en el orden de la pista; el último es la llegada.
  const order = gateOrder(board.reversed);
  let next = 0, finishTime = -1;
  for (let i = 1; i < n && next < order.length; i++) {
    const gate = GATES[order[next]];
    if (segmentHitsGate(g, i - 1, i, gate)) {
      if (next === order.length - 1) finishTime = g.t[i];
      next++;
    }
  }
  if (next < order.length) return { code: "recorrido", detail: "le falta el checkpoint " + order[next] };
  if (finishTime < g.t[n - 1] - LIMITS.finishWindow) return { code: "recorrido", detail: "siguió después de la llegada" };

  return null;
}

/** Aceleración de costado máxima esperable con esas gomas y ese kit, con margen. */
function lateralAllowed(grip, area) {
  const ref = GRIP_REFERENCE;
  const scale = (grip / ref.grip) * (1 + DOWNFORCE_PER_AREA * area) / (1 + DOWNFORCE_PER_AREA * ref.area);
  return ref.lateral * scale * LIMITS.lateralMargin;
}

/**
 * Aceleración de costado de la vuelta (percentil 99, m/s²). La trayectoria se
 * interpola cada 0,05 s (así no importa cuándo se tomó cada muestra), se suaviza
 * (dos promedios de ±0,1 s) y se deriva; solo cuenta arriba de 5 m/s.
 */
function lateralP99(g) {
  const n = g.count, step = 0.05, half = 2;
  const m = Math.floor(g.t[n - 1] / step);
  if (m < 10) return 0;

  let x = new Float64Array(m), z = new Float64Array(m);
  for (let i = 0, j = 0; i < m; i++) {
    const tc = i * step;
    while (j < n - 2 && g.t[j + 1] < tc) j++;
    const t0 = g.t[j], t1 = g.t[j + 1];
    const u = t1 > t0 ? Math.min(1, Math.max(0, (tc - t0) / (t1 - t0))) : 0;
    x[i] = g.x[j] + (g.x[j + 1] - g.x[j]) * u;
    z[i] = g.z[j] + (g.z[j + 1] - g.z[j]) * u;
  }

  for (let pass = 0; pass < 2; pass++) {
    x = movingAverage(x, half);
    z = movingAverage(z, half);
  }

  const values = [];
  for (let i = 2; i < m - 2; i++) {
    const vx = (x[i + 1] - x[i - 1]) / (2 * step), vz = (z[i + 1] - z[i - 1]) / (2 * step);
    const v = Math.hypot(vx, vz);
    if (v < 5) continue;
    const ax = ((x[i + 2] - x[i]) - (x[i] - x[i - 2])) / (4 * step * step);
    const az = ((z[i + 2] - z[i]) - (z[i] - z[i - 2])) / (4 * step * step);
    values.push(Math.abs(vx * az - vz * ax) / v);
  }
  if (values.length === 0) return 0;
  values.sort((a, b) => a - b);
  return values[Math.floor(values.length * 0.99)];
}

function movingAverage(values, half) {
  const out = new Float64Array(values.length);
  let sum = 0, lo = 0, hi = -1;
  for (let i = 0; i < values.length; i++) {
    while (hi < Math.min(values.length - 1, i + half)) sum += values[++hi];
    while (lo < i - half) sum -= values[lo++];
    out[i] = sum / (hi - lo + 1);
  }
  return out;
}

/** Orden de los checkpoints de la escena en la carrera (igual que RaceCheckpointManager.SetReversed). */
function gateOrder(reversed) {
  const count = GATES.length;
  const order = new Array(count);
  for (let index = 0; index < count; index++) {
    const position = !reversed || index === count - 1 ? index : count - 2 - index;
    order[position] = index;
  }
  return order;
}

/** Velocidad promedio en ventanas de `window` s (suaviza el temblequeo entre muestras). */
function windowSpeeds(g, window) {
  const out = [];
  const n = g.count;
  let j = 0, distance = 0;
  for (let i = 0; i < n; i++) {
    if (j < i) { j = i; distance = 0; }
    while (j < n - 1 && g.t[j] - g.t[i] < window) {
      j++;
      distance += Math.hypot(g.x[j] - g.x[j - 1], g.y[j] - g.y[j - 1], g.z[j] - g.z[j - 1]);
    }
    const dt = g.t[j] - g.t[i];
    if (dt >= window * 0.75) out.push({ t: g.t[i] + dt / 2, v: distance / dt });
    if (i + 1 <= j) distance -= Math.hypot(g.x[i + 1] - g.x[i], g.y[i + 1] - g.y[i], g.z[i + 1] - g.z[i]);
  }
  return out;
}

/** True si el tramo entre dos muestras toca el checkpoint (agrandado por el tamaño del kart). */
function segmentHitsGate(g, a, b, gate) {
  const axes = [gate.r, gate.u, gate.f];
  let tmin = 0, tmax = 1;
  for (let k = 0; k < 3; k++) {
    const ax = axes[k];
    const h = gate.h[k] + LIMITS.gateMargin[k];
    const start = (g.x[a] - gate.c[0]) * ax[0] + (g.y[a] - gate.c[1]) * ax[1] + (g.z[a] - gate.c[2]) * ax[2];
    const delta = (g.x[b] - g.x[a]) * ax[0] + (g.y[b] - g.y[a]) * ax[1] + (g.z[b] - g.z[a]) * ax[2];
    if (Math.abs(delta) < 1e-9) {
      if (Math.abs(start) > h) return false;
      continue;
    }
    let t1 = (-h - start) / delta, t2 = (h - start) / delta;
    if (t1 > t2) { const tmp = t1; t1 = t2; t2 = tmp; }
    if (t1 > tmin) tmin = t1;
    if (t2 < tmax) tmax = t2;
    if (tmin > tmax) return false;
  }
  return true;
}

// ---------------------------------------------------------------------------
// Copias
// ---------------------------------------------------------------------------

const PROFILE_POINTS = 300;

/** Trayectoria suavizada (±0,5 s) en 300 puntos de tiempo normalizado: así no importa si la estiraron. */
function profile(g) {
  const n = g.count, total = g.t[n - 1] || 1;
  const out = new Float64Array(PROFILE_POINTS * 2);
  let lo = 0, hi = 0, sx = 0, sz = 0;
  for (let k = 0; k < PROFILE_POINTS; k++) {
    const center = (k + 0.5) / PROFILE_POINTS * total;
    while (hi < n && g.t[hi] <= center + 0.5) { sx += g.x[hi]; sz += g.z[hi]; hi++; }
    while (lo < hi && g.t[lo] < center - 0.5) { sx -= g.x[lo]; sz -= g.z[lo]; lo++; }
    const count = Math.max(1, hi - lo);
    out[k * 2] = sx / count;
    out[k * 2 + 1] = sz / count;
  }
  return out;
}

function profileDistance(a, b) {
  let sum = 0;
  for (let k = 0; k < PROFILE_POINTS; k++) sum += Math.hypot(a[k * 2] - b[k * 2], a[k * 2 + 1] - b[k * 2 + 1]);
  return sum / PROFILE_POINTS;
}

/** Busca entre las vueltas de otros con tiempo parecido una casi igual. Devuelve el detalle o null. */
async function findCopy(api, projectId, boardId, playerId, ghost, score) {
  const page = await api.getScores(projectId, boardId, 0, 200);
  const candidates = page
    .filter(e => e.playerId !== playerId && Math.abs(e.score - score) <= score * LIMITS.copyTimeRange)
    .sort((a, b) => Math.abs(a.score - score) - Math.abs(b.score - score))
    .slice(0, LIMITS.copyCandidates);
  if (candidates.length === 0) return null;

  const mine = profile(ghost);
  const others = await Promise.all(candidates.map(e => api.getPublic(projectId, e.playerId, ghostKey(boardId)).catch(() => null)));

  for (let i = 0; i < candidates.length; i++) {
    if (typeof others[i] !== "string") continue;
    let other;
    try {
      other = parseGhost(gunzip(decodeBase64(others[i], LIMITS.maxBase64), 12 + 32 * LIMITS.maxSamples), LIMITS.maxSamples);
    } catch (error) {
      continue;
    }
    const distance = profileDistance(mine, profile(other));
    if (distance < LIMITS.copyDistance) return "igual a la de " + candidates[i].playerId + " (" + distance.toFixed(2) + " m)";
  }
  return null;
}

// ---------------------------------------------------------------------------
// Límite de envíos
// ---------------------------------------------------------------------------

/** Anota un envío en un dato protegido (el jugador no lo puede borrar). Devuelve el código si se pasó. */
async function countSend(api, projectId, playerId, key, maxSends) {
  const now = Date.now() / 1000;
  const item = await api.getProtected(projectId, playerId, key);
  let times = item && item.value && Array.isArray(item.value.t) ? item.value.t.filter(t => typeof t === "number" && now - t < LIMITS.sendsWindow) : [];

  if (times.length > 0 && now - times[times.length - 1] < LIMITS.minSendGap) return "muchos";
  if (times.length >= maxSends) return "muchos";

  times.push(now);
  try {
    await api.setProtected(projectId, playerId, key, { t: times }, item ? item.writeLock : undefined);
  } catch (error) {
    // Otro envío al mismo tiempo (writeLock): uno por vez.
    if (error.response && error.response.status === 409) return "ocupado";
    throw error;
  }
  return null;
}

// ---------------------------------------------------------------------------
// Servicios (como Cloud Code: serviceToken)
// ---------------------------------------------------------------------------

function services(context) {
  const config = {
    headers: { Authorization: "Bearer " + context.serviceToken, "Content-Type": "application/json" },
    timeout: 8000
  };
  const save = "https://cloud-save.services.api.unity.com/v1/data/projects/";
  const boards = "https://leaderboards.services.api.unity.com/v1/projects/";

  return {
    async getScore(projectId, boardId, playerId) {
      try {
        const res = await axios.get(boards + projectId + "/leaderboards/" + boardId + "/scores/players/" + playerId, config);
        return res.data;
      } catch (error) {
        if (error.response && error.response.status === 404) return null;
        throw error;
      }
    },
    async getScores(projectId, boardId, offset, limit) {
      const res = await axios.get(boards + projectId + "/leaderboards/" + boardId + "/scores?offset=" + offset + "&limit=" + limit, config);
      return (res.data && res.data.results) || [];
    },
    async addScore(projectId, boardId, playerId, score, metadata) {
      const res = await axios.post(boards + projectId + "/leaderboards/" + boardId + "/scores/players/" + playerId, { score, metadata }, config);
      return res.data;
    },
    async getPublic(projectId, playerId, key) {
      const res = await axios.get(save + projectId + "/players/" + playerId + "/public/items?keys=" + encodeURIComponent(key), config);
      const item = res.data && res.data.results && res.data.results.find(r => r.key === key);
      return item ? item.value : null;
    },
    async setPublic(projectId, playerId, key, value) {
      await axios.post(save + projectId + "/players/" + playerId + "/public/items", { key, value }, config);
    },
    async getProtected(projectId, playerId, key) {
      const res = await axios.get(save + projectId + "/players/" + playerId + "/protected/items?keys=" + encodeURIComponent(key), config);
      return (res.data && res.data.results && res.data.results.find(r => r.key === key)) || null;
    },
    async setProtected(projectId, playerId, key, value, writeLock) {
      const body = { key, value };
      if (writeLock) body.writeLock = writeLock;
      await axios.post(save + projectId + "/players/" + playerId + "/protected/items", body, config);
    }
  };
}

// ---------------------------------------------------------------------------
// Lectura del fantasma en el servidor, sin módulos de Node (Cloud Code JS no
// garantiza Buffer ni zlib): Base64 -> GZip (RFC 1952) -> Deflate (RFC 1951)
// -> GhostRecording v1 (el formato binario de GhostRecording.Write en C#).
// Todo con límites de tamaño, para que un dato armado no pueda colgar el script.
// ---------------------------------------------------------------------------

const B64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
const B64_INDEX = (() => {
  const table = new Int16Array(128).fill(-1);
  for (let i = 0; i < B64.length; i++) table[B64.charCodeAt(i)] = i;
  return table;
})();

function decodeBase64(text, maxBytes) {
  if (typeof text !== "string") throw new Error("fantasma: no es texto");
  if (text.length % 4 !== 0) throw new Error("fantasma: Base64 roto");

  let padding = 0;
  if (text.endsWith("==")) padding = 2;
  else if (text.endsWith("=")) padding = 1;

  const size = (text.length / 4) * 3 - padding;
  if (size > maxBytes) throw new Error("fantasma: demasiado grande");

  const out = new Uint8Array(size);
  let o = 0;

  for (let i = 0; i < text.length; i += 4) {
    const v = [0, 0, 0, 0];

    for (let k = 0; k < 4; k++) {
      const c = text.charCodeAt(i + k);
      const last = i + 4 === text.length;

      if (c === 61 && last && k >= 4 - padding) { v[k] = 0; continue; } // '='
      if (c >= 128 || B64_INDEX[c] < 0) throw new Error("fantasma: Base64 roto");
      v[k] = B64_INDEX[c];
    }

    const n = (v[0] << 18) | (v[1] << 12) | (v[2] << 6) | v[3];
    if (o < size) out[o++] = (n >> 16) & 255;
    if (o < size) out[o++] = (n >> 8) & 255;
    if (o < size) out[o++] = n & 255;
  }

  return out;
}

// ---------- Deflate (como puff.c de zlib) ----------

const LEN_BASE = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
const LEN_EXTRA = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
const DIST_BASE = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
const DIST_EXTRA = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];
const CODE_ORDER = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

function buildHuffman(lengths, offset, n) {
  const count = new Int16Array(16);
  const symbol = new Int16Array(n);
  const offs = new Int16Array(16);

  for (let s = 0; s < n; s++) count[lengths[offset + s]]++;
  if (count[0] === n) return { count, symbol, empty: true };

  let left = 1;
  for (let len = 1; len < 16; len++) {
    left <<= 1;
    left -= count[len];
    if (left < 0) throw new Error("fantasma: Huffman inválido");
  }

  for (let len = 1; len < 15; len++) offs[len + 1] = offs[len] + count[len];
  for (let s = 0; s < n; s++) {
    const len = lengths[offset + s];
    if (len !== 0) symbol[offs[len]++] = s;
  }

  return { count, symbol, empty: false };
}

function inflateRaw(src, start, maxOut) {
  let pos = start;
  let bitBuf = 0;
  let bitCnt = 0;
  let out = new Uint8Array(Math.min(maxOut, 65536));
  let outLen = 0;

  function bits(n) {
    while (bitCnt < n) {
      if (pos >= src.length) throw new Error("fantasma: datos cortados");
      bitBuf |= src[pos++] << bitCnt;
      bitCnt += 8;
    }
    const value = bitBuf & ((1 << n) - 1);
    bitBuf >>>= n;
    bitCnt -= n;
    return value;
  }

  function ensure(extra) {
    if (outLen + extra > maxOut) throw new Error("fantasma: demasiado grande");
    if (outLen + extra <= out.length) return;
    let size = out.length;
    while (size < outLen + extra) size *= 2;
    const bigger = new Uint8Array(Math.min(size, maxOut));
    bigger.set(out.subarray(0, outLen));
    out = bigger;
  }

  function decode(h) {
    let code = 0, first = 0, index = 0;
    for (let len = 1; len < 16; len++) {
      code |= bits(1);
      const count = h.count[len];
      if (code - count < first) return h.symbol[index + (code - first)];
      index += count;
      first += count;
      first <<= 1;
      code <<= 1;
    }
    throw new Error("fantasma: código inválido");
  }

  function codes(lencode, distcode) {
    for (;;) {
      let sym = decode(lencode);
      if (sym < 256) {
        ensure(1);
        out[outLen++] = sym;
        continue;
      }
      if (sym === 256) return;

      sym -= 257;
      if (sym >= 29) throw new Error("fantasma: largo inválido");
      const len = LEN_BASE[sym] + bits(LEN_EXTRA[sym]);

      if (distcode.empty) throw new Error("fantasma: sin distancias");
      const dsym = decode(distcode);
      if (dsym >= 30) throw new Error("fantasma: distancia inválida");
      const dist = DIST_BASE[dsym] + bits(DIST_EXTRA[dsym]);
      if (dist > outLen) throw new Error("fantasma: distancia inválida");

      ensure(len);
      for (let k = 0; k < len; k++) {
        out[outLen] = out[outLen - dist];
        outLen++;
      }
    }
  }

  const fixed = (() => {
    const lengths = new Uint8Array(288 + 30);
    for (let s = 0; s < 144; s++) lengths[s] = 8;
    for (let s = 144; s < 256; s++) lengths[s] = 9;
    for (let s = 256; s < 280; s++) lengths[s] = 7;
    for (let s = 280; s < 288; s++) lengths[s] = 8;
    for (let s = 288; s < 318; s++) lengths[s] = 5;
    return { len: buildHuffman(lengths, 0, 288), dist: buildHuffman(lengths, 288, 30) };
  })();

  let last = 0;
  let blocks = 0;

  do {
    if (++blocks > 10000) throw new Error("fantasma: demasiados bloques");
    last = bits(1);
    const type = bits(2);

    if (type === 0) {
      // Sin comprimir: se descarta lo que queda del byte.
      bitBuf = 0;
      bitCnt = 0;
      if (pos + 4 > src.length) throw new Error("fantasma: datos cortados");
      const len = src[pos] | (src[pos + 1] << 8);
      const nlen = src[pos + 2] | (src[pos + 3] << 8);
      pos += 4;
      if (len !== (~nlen & 0xffff)) throw new Error("fantasma: bloque inválido");
      if (pos + len > src.length) throw new Error("fantasma: datos cortados");
      ensure(len);
      out.set(src.subarray(pos, pos + len), outLen);
      outLen += len;
      pos += len;
    } else if (type === 1) {
      codes(fixed.len, fixed.dist);
    } else if (type === 2) {
      const nlen = bits(5) + 257;
      const ndist = bits(5) + 1;
      const ncode = bits(4) + 4;
      if (nlen > 286 || ndist > 30) throw new Error("fantasma: bloque inválido");

      const lengths = new Uint8Array(320);
      for (let k = 0; k < ncode; k++) lengths[CODE_ORDER[k]] = bits(3);
      for (let k = ncode; k < 19; k++) lengths[CODE_ORDER[k]] = 0;
      const lencode0 = buildHuffman(lengths, 0, 19);
      if (lencode0.empty) throw new Error("fantasma: bloque inválido");

      const all = new Uint8Array(nlen + ndist);
      let index = 0;
      while (index < nlen + ndist) {
        let sym = decode(lencode0);
        if (sym < 16) {
          all[index++] = sym;
        } else {
          let len = 0;
          let repeat;
          if (sym === 16) {
            if (index === 0) throw new Error("fantasma: bloque inválido");
            len = all[index - 1];
            repeat = 3 + bits(2);
          } else if (sym === 17) {
            repeat = 3 + bits(3);
          } else {
            repeat = 11 + bits(7);
          }
          if (index + repeat > nlen + ndist) throw new Error("fantasma: bloque inválido");
          while (repeat--) all[index++] = len;
        }
      }

      if (all[256] === 0) throw new Error("fantasma: bloque sin fin");
      const lencode = buildHuffman(all, 0, nlen);
      const distcode = buildHuffman(all, nlen, ndist);
      codes(lencode, distcode);
    } else {
      throw new Error("fantasma: bloque inválido");
    }
  } while (!last);

  return { data: out.subarray(0, outLen), end: pos };
}

// ---------- GZip ----------

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  return table;
})();

function crc32(data) {
  let c = 0xffffffff;
  for (let i = 0; i < data.length; i++) c = CRC_TABLE[(c ^ data[i]) & 255] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function gunzip(src, maxOut) {
  if (src.length < 18 || src[0] !== 0x1f || src[1] !== 0x8b || src[2] !== 8) throw new Error("fantasma: no es GZip");
  const flags = src[3];
  if (flags & 0xe0) throw new Error("fantasma: GZip inválido");
  let pos = 10;

  if (flags & 4) {
    if (pos + 2 > src.length) throw new Error("fantasma: datos cortados");
    pos += 2 + (src[pos] | (src[pos + 1] << 8));
  }
  if (flags & 8) { while (pos < src.length && src[pos] !== 0) pos++; pos++; }
  if (flags & 16) { while (pos < src.length && src[pos] !== 0) pos++; pos++; }
  if (flags & 2) pos += 2;
  if (pos >= src.length) throw new Error("fantasma: datos cortados");

  const result = inflateRaw(src, pos, maxOut);
  const end = result.end;
  if (end + 8 > src.length) throw new Error("fantasma: datos cortados");

  const crc = (src[end] | (src[end + 1] << 8) | (src[end + 2] << 16) | (src[end + 3] << 24)) >>> 0;
  const size = (src[end + 4] | (src[end + 5] << 8) | (src[end + 6] << 16) | (src[end + 7] << 24)) >>> 0;

  if (crc !== crc32(result.data) || size !== result.data.length) throw new Error("fantasma: datos dañados");
  return result.data;
}

// ---------- GhostRecording v1 ----------

const SAMPLE_BYTES = 32;

function parseGhost(bytes, maxSamples) {
  if (bytes.length < 12) throw new Error("fantasma: muy corto");
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);

  const version = view.getInt32(0, true);
  if (version !== 1) throw new Error("fantasma: versión desconocida");

  const lapTime = view.getFloat32(4, true);
  const count = view.getInt32(8, true);
  if (count < 2 || count > maxSamples) throw new Error("fantasma: cantidad de muestras inválida");
  if (bytes.length !== 12 + count * SAMPLE_BYTES) throw new Error("fantasma: tamaño inválido");

  const t = new Float64Array(count);
  const x = new Float64Array(count);
  const y = new Float64Array(count);
  const z = new Float64Array(count);
  const qx = new Float64Array(count);
  const qy = new Float64Array(count);
  const qz = new Float64Array(count);
  const qw = new Float64Array(count);

  for (let i = 0; i < count; i++) {
    const o = 12 + i * SAMPLE_BYTES;
    t[i] = view.getFloat32(o, true);
    x[i] = view.getFloat32(o + 4, true);
    y[i] = view.getFloat32(o + 8, true);
    z[i] = view.getFloat32(o + 12, true);
    qx[i] = view.getFloat32(o + 16, true);
    qy[i] = view.getFloat32(o + 20, true);
    qz[i] = view.getFloat32(o + 24, true);
    qw[i] = view.getFloat32(o + 28, true);

    for (const v of [t[i], x[i], y[i], z[i], qx[i], qy[i], qz[i], qw[i]]) {
      if (!Number.isFinite(v)) throw new Error("fantasma: número inválido");
    }
  }

  if (!Number.isFinite(lapTime)) throw new Error("fantasma: tiempo inválido");
  return { lapTime, count, t, x, y, z, qx, qy, qz, qw };
}


// Para las pruebas locales (Cloud Code no usa esto).
module.exports.internals = { decodeBase64, gunzip, parseGhost, validateLap, readInput, cleanName, profile, profileDistance, gateOrder, windowSpeeds, lateralP99, lateralAllowed, countSend, submit, GATES, ENGINES, WHEELS, WHEEL_GRIP, AERO_AREA, LIMITS };
