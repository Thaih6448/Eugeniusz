"""Dependency-free Python bindings. Native libraries and model weights are separate."""
import ctypes as C
import os
from pathlib import Path
import sys
from dataclasses import dataclass

MAX_OPTIONS = 26
CHOICE, SCORE, TRUTH = 0, 1, 2


class _Question(C.Structure):
    _fields_ = [("kind", C.c_int32), ("instructions", C.c_char_p),
                ("criteria", C.POINTER(C.c_char_p)), ("count", C.c_int32),
                ("temperature", C.c_double), ("min_confidence", C.c_double)]


class _Result(C.Structure):
    _fields_ = [("kind", C.c_int32), ("count", C.c_int32), ("choice", C.c_int32),
                ("abstained", C.c_int32), ("value", C.c_double),
                ("confidence", C.c_double), ("certainty", C.c_double),
                ("probabilities", C.c_double * MAX_OPTIONS), ("logits", C.c_double * MAX_OPTIONS)]


class _Options(C.Structure):
    _fields_ = [("context_size", C.c_uint32), ("threads", C.c_int32), ("gpu_layers", C.c_int32)]


class _Metrics(C.Structure):
    _fields_ = [(name, C.c_double) for name in ("nll", "brier", "ece", "accuracy")]


@dataclass(frozen=True)
class Question:
    instructions: str
    criteria: tuple = ()
    kind: int = CHOICE
    temperature: float = 1.0
    min_confidence: float = 0.0


@dataclass(frozen=True)
class Result:
    kind: int
    choice: int
    value: float
    confidence: float
    certainty: float
    abstained: bool
    probabilities: tuple
    logits: tuple

    @staticmethod
    def _from_native(r):
        return Result(r.kind, r.choice, r.value, r.confidence, r.certainty,
                      bool(r.abstained), tuple(r.probabilities[:r.count]), tuple(r.logits[:r.count]))


def _utf8(text):
    if not isinstance(text, str) or "\0" in text:
        raise ValueError("Expected a string without embedded NUL characters")
    return text.encode("utf-8")


def _library_path(directory, name):
    root = Path(directory).resolve()
    names = ([name + ".dll", "lib" + name + ".dll"] if sys.platform == "win32"
             else ["lib" + name + (".dylib" if sys.platform == "darwin" else ".so")])
    for parent in (root, root / "bin", root / "lib", root / "bin" / "Release"):
        for filename in names:
            if (parent / filename).is_file():
                return parent / filename
    raise FileNotFoundError(f"Cannot locate {name} in {root}; pass the native build/install directory")


class Runtime:
    def __init__(self, library_dir=None):
        self.directory = library_dir or os.environ.get("EUGENIUSZ_LIBRARY_DIR", ".")
        path = _library_path(self.directory, "eugeniusz")
        self._dll_dirs = []
        if sys.platform == "win32":
            for directory in {path.parent, Path(self.directory).resolve() / "bin"}:
                if directory.is_dir():
                    self._dll_dirs.append(os.add_dll_directory(str(directory)))
        self.core = C.CDLL(str(path))
        lib = self.core
        lib.eg_abi_version.restype = C.c_uint32
        if lib.eg_abi_version() != 1:
            raise RuntimeError("Unsupported Eugeniusz ABI version")
        lib.eg_last_error.restype = C.c_char_p
        lib.eg_from_logits.argtypes = [C.c_int32, C.POINTER(C.c_double), C.c_int32, C.c_double, C.c_double, C.POINTER(_Result)]
        lib.eg_evaluate_batch.argtypes = [C.c_void_p, C.c_char_p, C.POINTER(_Question), C.c_int32, C.POINTER(_Result)]
        lib.eg_engine_destroy.argtypes = [C.c_void_p]
        lib.eg_engine_destroy.restype = None
        lib.eg_fit_temperature.argtypes = [C.POINTER(C.c_double), C.POINTER(C.c_int32), C.c_int32, C.c_int32, C.POINTER(C.c_double)]
        lib.eg_measure.argtypes = [C.POINTER(C.c_double), C.POINTER(C.c_int32), C.c_int32, C.c_int32, C.c_double, C.c_int32, C.POINTER(_Metrics)]
        lib.eg_conformal_fit.argtypes = [C.POINTER(C.c_double), C.c_int32, C.c_double, C.POINTER(C.c_double)]
        lib.eg_conformal_set.argtypes = [C.POINTER(C.c_double), C.c_int32, C.c_double, C.POINTER(C.c_uint32)]

    def _check(self, status):
        if status:
            raise RuntimeError(self.core.eg_last_error().decode("utf-8", errors="replace"))

    def from_logits(self, logits, kind=CHOICE, temperature=1, min_confidence=0):
        values = (C.c_double * len(logits))(*logits)
        result = _Result()
        self._check(self.core.eg_from_logits(kind, values, len(logits), temperature, min_confidence, C.byref(result)))
        return Result._from_native(result)

    @staticmethod
    def _dataset(logits, labels):
        if not logits or len(logits) != len(labels):
            raise ValueError("Nonempty logits and matching labels are required")
        classes = len(logits[0])
        if any(len(row) != classes for row in logits):
            raise ValueError("Logits must be a rectangular matrix")
        if any(not isinstance(label, int) or label < 0 or label >= classes for label in labels):
            raise ValueError("Labels must be integer class indices")
        return (C.c_double * (len(logits) * classes))(*(x for row in logits for x in row)), (C.c_int32 * len(labels))(*labels), classes

    def fit_temperature(self, logits, labels):
        values, targets, classes = self._dataset(logits, labels)
        result = C.c_double()
        self._check(self.core.eg_fit_temperature(values, targets, len(labels), classes, C.byref(result)))
        return result.value

    def measure(self, logits, labels, temperature=1, bins=10):
        values, targets, classes = self._dataset(logits, labels)
        result = _Metrics()
        self._check(self.core.eg_measure(values, targets, len(labels), classes, temperature, bins, C.byref(result)))
        return {name: getattr(result, name) for name, _ in _Metrics._fields_}

    def conformal_fit(self, true_probabilities, alpha=.1):
        values = (C.c_double * len(true_probabilities))(*true_probabilities)
        result = C.c_double()
        self._check(self.core.eg_conformal_fit(values, len(values), alpha, C.byref(result)))
        return result.value

    def conformal_set(self, probabilities, quantile):
        values = (C.c_double * len(probabilities))(*probabilities)
        mask = C.c_uint32()
        self._check(self.core.eg_conformal_set(values, len(values), quantile, C.byref(mask)))
        return tuple(i for i in range(len(values)) if mask.value & (1 << i))

    def load_model(self, path, context_size=4096, threads=4, gpu_layers=0, system_prompt=None):
        if not (256 <= context_size <= 131072 and 1 <= threads <= 1024 and 0 <= gpu_layers <= 2147483647):
            raise ValueError("Invalid model options")
        provider = C.CDLL(str(_library_path(self.directory, "eugeniusz_llama")))
        provider.eg_llama_create.argtypes = [C.c_char_p, C.POINTER(_Options), C.POINTER(C.c_void_p), C.c_char_p, C.c_uint32]
        options = _Options(context_size, threads, gpu_layers)
        handle, error = C.c_void_p(), C.create_string_buffer(1024)
        if system_prompt is None:
            status = provider.eg_llama_create(_utf8(str(path)), C.byref(options), C.byref(handle), error, len(error))
        else:
            provider.eg_llama_create_with_system_prompt.argtypes = [C.c_char_p, C.POINTER(_Options), C.c_char_p, C.POINTER(C.c_void_p), C.c_char_p, C.c_uint32]
            status = provider.eg_llama_create_with_system_prompt(_utf8(str(path)), C.byref(options), _utf8(system_prompt), C.byref(handle), error, len(error))
        if status:
            raise RuntimeError(error.value.decode("utf-8", errors="replace"))
        return Engine(self, provider, handle)


class Engine:
    def __init__(self, runtime, provider, handle):
        import threading
        self.runtime, self._provider, self._handle = runtime, provider, handle
        self._lock = threading.Lock()

    def close(self):
        with self._lock:
            if self._handle:
                self.runtime.core.eg_engine_destroy(self._handle)
                self._handle = None

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()

    def __del__(self):
        if hasattr(self, "_lock"):
            self.close()

    def evaluate(self, state, questions):
        if not questions:
            raise ValueError("Questions must not be empty")
        native, keepalive = [], []
        for q in questions:
            criteria = (C.c_char_p * len(q.criteria))(*(_utf8(s) for s in q.criteria)) if q.criteria else None
            keepalive.append(criteria)
            native.append(_Question(q.kind, _utf8(q.instructions), criteria, len(q.criteria), q.temperature, q.min_confidence))
        inputs, outputs = (_Question * len(native))(*native), (_Result * len(native))()
        with self._lock:
            if not self._handle:
                raise RuntimeError("Engine is closed")
            self.runtime._check(self.runtime.core.eg_evaluate_batch(self._handle, _utf8(state), inputs, len(native), outputs))
        return [Result._from_native(result) for result in outputs]

    def choice(self, state, instructions, criteria, temperature=1, min_confidence=0):
        return self.evaluate(state, [Question(instructions, tuple(criteria), CHOICE, temperature, min_confidence)])[0]

    def score(self, state, instructions, levels, temperature=1, min_confidence=0):
        return self.evaluate(state, [Question(instructions, tuple(levels), SCORE, temperature, min_confidence)])[0]

    def truth(self, state, instructions, temperature=1, min_confidence=0):
        return self.evaluate(state, [Question(instructions, (), TRUTH, temperature, min_confidence)])[0]

    def generate(self, system_prompt, prompt, max_tokens=1024):
        """Bounded greedy text completion; separate from the typed decision API."""
        if not isinstance(max_tokens, int) or not 1 <= max_tokens <= 4096:
            raise ValueError("max_tokens must be in 1..4096")
        function = self._provider.eg_llama_generate
        function.argtypes = [C.c_void_p, C.c_char_p, C.c_char_p, C.c_uint32, C.c_char_p, C.c_uint32, C.c_char_p, C.c_uint32]
        output, error = C.create_string_buffer(65536), C.create_string_buffer(1024)
        with self._lock:
            if not self._handle:
                raise RuntimeError("Engine is closed")
            status = function(self._handle, _utf8(system_prompt), _utf8(prompt), max_tokens, output, len(output), error, len(error))
            if status:
                raise RuntimeError(error.value.decode("utf-8", errors="replace"))
            return output.value.decode("utf-8")
