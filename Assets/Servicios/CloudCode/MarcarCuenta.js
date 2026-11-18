/**
 * MarcarCuenta — Cloud Code (JavaScript) de ApexRun.
 *
 * Anota al jugador en la lista de cuentas con usuario y contraseña, para verlas
 * todas juntas en el panel de Unity (Cloud Save > Custom Items > "cuentas"). El
 * panel de jugadores no deja buscar por usuario ni lista bien estas cuentas.
 *
 * Lo llama el juego al crear la cuenta, al entrar y al abrirse con la sesión
 * guardada (PlayerAccount.ReportAsync). La lista es privada: solo la lee el
 * servidor y el panel, nunca el juego. La guarda el script con el token de
 * servicio porque Access Control no deja escribir al jugador.
 *
 * El usuario lo manda el juego y no se puede comprobar desde acá (haría falta la
 * Admin API): sirve para verlo en el panel, no para dar permisos.
 */

const axios = require("axios-1.6");

const LIST = "cuentas";
const MIN_GAP = 60;  // segundos: si el jugador ya se anotó hace menos, no se reescribe

module.exports = async ({ params, context, logger }) => {
  try {
    const { projectId, playerId } = context;
    const username = cleanUsername(params.usuario);

    if (!username) return { ok: false, codigo: "usuario" };

    const api = services(context);
    const item = await api.get(projectId, playerId);
    const old = item && item.value && typeof item.value === "object" ? item.value : {};
    const now = new Date().toISOString();

    if (old.usuario === username && old.ultima && Date.now() - Date.parse(old.ultima) < MIN_GAP * 1000)
      return { ok: true, codigo: "ok" };

    const value = {
      usuario: username,
      creada: typeof old.creada === "string" ? old.creada : now,
      ultima: now,
      ingresos: (typeof old.ingresos === "number" ? old.ingresos : 0) + 1
    };

    const body = { key: playerId, value };
    if (item && item.writeLock) body.writeLock = item.writeLock;

    try {
      await api.set(projectId, body);
    } catch (error) {
      if (error.response && error.response.status === 409) return { ok: true, codigo: "ok" };
      throw error;
    }
    return { ok: true, codigo: "ok" };
  } catch (error) {
    logger.error("MarcarCuenta: error interno", { "error.message": error && error.message });
    return { ok: false, codigo: "servidor" };
  }
};

module.exports.params = {
  usuario: { type: "String", required: true }
};

/** Las reglas de usuario de Unity: 3 a 20, letras sin acento, números y . - @ _ (sin distinguir mayúsculas). */
function cleanUsername(value) {
  if (typeof value !== "string") return "";
  const name = value.trim().toLowerCase();
  return /^[a-z0-9.\-@_]{3,20}$/.test(name) ? name : "";
}

function services(context) {
  const config = {
    headers: { Authorization: "Bearer " + context.serviceToken, "Content-Type": "application/json" },
    timeout: 8000
  };
  const base = (projectId) => "https://cloud-save.services.api.unity.com/v1/data/projects/" + projectId + "/custom/" + LIST + "/private/items";

  return {
    async get(projectId, playerId) {
      try {
        const res = await axios.get(base(projectId) + "?keys=" + encodeURIComponent(playerId), config);
        return (res.data && res.data.results && res.data.results.find(r => r.key === playerId)) || null;
      } catch (error) {
        if (error.response && error.response.status === 404) return null;
        throw error;
      }
    },
    async set(projectId, body) {
      await axios.post(base(projectId), body, config);
    }
  };
}

module.exports.internals = { cleanUsername };
