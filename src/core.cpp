#include "eugeniusz/eugeniusz.h"
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <limits>
#include <mutex>
#include <stdexcept>
#include <vector>

struct eg_engine {
    eg_logits_fn backend;
    void *user;
    eg_destroy_fn destroy;
    std::mutex mutex;
};
namespace {
thread_local char last_error[1024] = {};
struct Failure : std::runtime_error {
    int32_t status;
    Failure(int32_t s, const char *message) : std::runtime_error(message), status(s) {}
};
void require(bool valid, const char *message) {
    if (!valid) throw Failure(EG_INVALID_ARGUMENT, message);
}
template<class F> int32_t guard(F &&fn) noexcept {
    last_error[0] = '\0';
    try { fn(); return EG_OK; }
    catch (const Failure &e) { std::snprintf(last_error, sizeof(last_error), "%s", e.what()); return e.status; }
    catch (const std::exception &e) { std::snprintf(last_error, sizeof(last_error), "%s", e.what()); return EG_INTERNAL_ERROR; }
    catch (...) { std::snprintf(last_error, sizeof(last_error), "Unknown native exception"); return EG_INTERNAL_ERROR; }
}
void validate(int32_t kind, int32_t n, double temperature, double threshold) {
    require(kind >= EG_CHOICE && kind <= EG_TRUTH, "Unknown question kind");
    require(n >= 2 && n <= EG_MAX_OPTIONS, "Expected 2 to 26 criteria");
    require(kind != EG_TRUTH || n == 2, "Truth requires exactly two criteria: false, true");
    require(std::isfinite(temperature) && temperature >= 0.05 && temperature <= 20, "Temperature must be in [0.05, 20]");
    require(std::isfinite(threshold) && threshold >= 0 && threshold <= 1, "Confidence threshold must be in [0, 1]");
}
eg_result distribution(int32_t kind, const double *logits, int32_t n, double t, double threshold) {
    validate(kind, n, t, threshold);
    require(logits != nullptr, "Logits must not be null");
    eg_result result{};
    result.kind = kind;
    result.count = n;
    double maximum = -std::numeric_limits<double>::infinity();
    for (int i = 0; i < n; ++i) {
        require(std::isfinite(logits[i]) && std::abs(logits[i]) <= 1e100, "Logits must be finite and have magnitude <= 1e100");
        maximum = std::max(maximum, logits[i]);
        result.logits[i] = logits[i];
    }
    double sum = 0;
    for (int i = 0; i < n; ++i) sum += result.probabilities[i] = std::exp((logits[i] - maximum) / t);
    double entropy = 0, expected = 0;
    for (int i = 0; i < n; ++i) {
        const double p = result.probabilities[i] /= sum;
        if (p > result.confidence) { result.confidence = p; result.choice = i; }
        if (p > 0) entropy -= p * std::log(p);
        expected += i * p;
    }
    result.certainty = std::clamp(1.0 - entropy / std::log(n), 0.0, 1.0);
    result.abstained = result.confidence < threshold;
    result.value = kind == EG_SCORE ? expected : kind == EG_TRUTH ? result.probabilities[1] : result.choice;
    return result;
}
const char *truth_criteria[] = {"False: the answer is no.", "True: the answer is yes."};
eg_question normalize(const eg_question &q) {
    eg_question result = q;
    if (q.kind == EG_TRUTH && !q.criteria && q.count == 0) {
        result.criteria = truth_criteria;
        result.count = 2;
    }
    validate(result.kind, result.count, result.temperature, result.min_confidence);
    require(result.instructions && result.instructions[0], "Instructions must not be empty");
    require(result.criteria != nullptr, "Criteria must not be null");
    for (int i = 0; i < result.count; ++i) {
        require(result.criteria[i] && result.criteria[i][0], "Criterion must not be empty");
        for (int j = 0; j < i; ++j)
            require(std::strcmp(result.criteria[i], result.criteria[j]) != 0, "Criteria must be distinct");
    }
    return result;
}
void validate_dataset(const double *logits, const int32_t *labels, int rows, int classes) {
    require(logits && labels && rows > 0, "Dataset must not be empty or null");
    require(classes >= 2 && classes <= EG_MAX_OPTIONS, "Expected 2 to 26 classes");
    for (int r = 0; r < rows; ++r) {
        require(labels[r] >= 0 && labels[r] < classes, "Label is out of range");
        for (int c = 0; c < classes; ++c) {
            double v = logits[static_cast<size_t>(r) * classes + c];
            require(std::isfinite(v) && std::abs(v) <= 1e100, "Dataset logits must be finite and have magnitude <= 1e100");
        }
    }
}
double nll(const double *logits, const int32_t *labels, int rows, int classes, double beta) {
    double loss = 0;
    for (int r = 0; r < rows; ++r) {
        const double *v = logits + static_cast<size_t>(r) * classes;
        double maximum = *std::max_element(v, v + classes), sum = 0;
        for (int c = 0; c < classes; ++c) sum += std::exp((v[c] - maximum) * beta);
        loss += (std::log(sum) + (maximum - v[labels[r]]) * beta) / rows;
    }
    return loss;
}
}
extern "C" {
uint32_t eg_abi_version(void) { return EG_ABI_VERSION; }
const char *eg_version(void) { return EG_VERSION; }
const char *eg_last_error(void) { return last_error; }
eg_question eg_question_default(void) { eg_question q{}; q.temperature = 1; return q; }
int32_t eg_engine_create(eg_logits_fn backend, void *user, eg_destroy_fn destroy, eg_engine **out) {
    return guard([&] { require(backend && out, "Backend and output must not be null"); *out = new eg_engine{backend, user, destroy, {}}; });
}
void eg_engine_destroy(eg_engine *engine) {
    if (!engine) return;
    try { if (engine->destroy) engine->destroy(engine->user); } catch (...) {}
    delete engine;
}
int32_t eg_evaluate(eg_engine *engine, const char *state, const eg_question *question, eg_result *out) {
    return eg_evaluate_batch(engine, state, question, 1, out);
}
int32_t eg_evaluate_batch(eg_engine *engine, const char *state, const eg_question *questions, int32_t count, eg_result *out) {
    return guard([&] {
        require(engine && state && questions && out && count > 0, "Engine, state, questions and output are required; batch must not be empty");
        std::vector<eg_question> normalized;
        normalized.reserve(count);
        for (int i = 0; i < count; ++i) normalized.push_back(normalize(questions[i]));
        std::vector<eg_result> results;
        results.reserve(count);
        std::lock_guard<std::mutex> lock(engine->mutex);
        for (const auto &q : normalized) {
            double logits[EG_MAX_OPTIONS];
            std::fill_n(logits, EG_MAX_OPTIONS, std::numeric_limits<double>::quiet_NaN());
            char error[1024]{};
            if (engine->backend(engine->user, state, &q, logits, error, sizeof(error))) {
                error[sizeof(error) - 1] = '\0';
                throw Failure(EG_BACKEND_ERROR, error[0] ? error : "Backend failed");
            }
            for (int i = 0; i < q.count; ++i)
                if (!std::isfinite(logits[i]) || std::abs(logits[i]) > 1e100)
                    throw Failure(EG_BACKEND_ERROR, "Backend returned invalid or missing logits");
            results.push_back(distribution(q.kind, logits, q.count, q.temperature, q.min_confidence));
        }
        std::copy(results.begin(), results.end(), out);
    });
}
int32_t eg_from_logits(int32_t kind, const double *logits, int32_t count, double t, double threshold, eg_result *out) {
    return guard([&] { require(out != nullptr, "Output must not be null"); *out = distribution(kind, logits, count, t, threshold); });
}
int32_t eg_fit_temperature(const double *logits, const int32_t *labels, int32_t rows, int32_t classes, double *temperature) {
    return guard([&] {
        require(temperature != nullptr, "Temperature output must not be null");
        validate_dataset(logits, labels, rows, classes);
        // NLL is convex in inverse temperature. Golden section also handles flat minima.
        double left = 0.05, right = 20, ratio = (std::sqrt(5.0) - 1) / 2;
        double a = right - ratio * (right - left), b = left + ratio * (right - left);
        double fa = nll(logits, labels, rows, classes, a), fb = nll(logits, labels, rows, classes, b);
        for (int step = 0; step < 100; ++step) {
            if (fa < fb) { right = b; b = a; fb = fa; a = right - ratio * (right - left); fa = nll(logits, labels, rows, classes, a); }
            else { left = a; a = b; fa = fb; b = left + ratio * (right - left); fb = nll(logits, labels, rows, classes, b); }
        }
        double best = 1, loss = nll(logits, labels, rows, classes, best);
        for (double beta : {0.05, 20.0, (left + right) / 2}) {
            double candidate = nll(logits, labels, rows, classes, beta);
            if (candidate < loss) { best = beta; loss = candidate; }
        }
        *temperature = 1 / best;
    });
}
int32_t eg_measure(const double *logits, const int32_t *labels, int32_t rows, int32_t classes, double t, int32_t bins, eg_metrics *out) {
    return guard([&] {
        require(out && bins > 0 && bins <= 10000, "Output required; bins must be in [1, 10000]");
        validate_dataset(logits, labels, rows, classes);
        validate(EG_CHOICE, classes, t, 0);
        eg_metrics metrics{};
        metrics.nll = nll(logits, labels, rows, classes, 1 / t);
        std::vector<double> confidence(bins), correct(bins), counts(bins);
        for (int r = 0; r < rows; ++r) {
            auto result = distribution(EG_CHOICE, logits + static_cast<size_t>(r) * classes, classes, t, 0);
            double hit = result.choice == labels[r] ? 1.0 : 0.0;
            metrics.accuracy += hit / rows;
            for (int c = 0; c < classes; ++c) {
                double difference = result.probabilities[c] - (c == labels[r] ? 1.0 : 0.0);
                metrics.brier += difference * difference / rows;
            }
            int bin = std::min(bins - 1, static_cast<int>(result.confidence * bins));
            confidence[bin] += result.confidence; correct[bin] += hit; counts[bin] += 1;
        }
        for (int i = 0; i < bins; ++i) if (counts[i]) metrics.ece += std::abs(confidence[i] - correct[i]) / rows;
        *out = metrics;
    });
}
int32_t eg_conformal_fit(const double *probabilities, int32_t count, double alpha, double *quantile) {
    return guard([&] {
        require(probabilities && quantile && count > 0, "Nonempty calibration probabilities and output required");
        require(std::isfinite(alpha) && alpha > 0 && alpha < 1, "Alpha must be in (0, 1)");
        std::vector<double> scores;
        scores.reserve(count);
        for (int i = 0; i < count; ++i) {
            require(std::isfinite(probabilities[i]) && probabilities[i] >= 0 && probabilities[i] <= 1, "Probability must be in [0, 1]");
            scores.push_back(1 - probabilities[i]);
        }
        const double rank = std::ceil((static_cast<double>(count) + 1) * (1 - alpha));
        if (rank > count) { *quantile = 1; return; }
        auto index = static_cast<size_t>(rank) - 1;
        std::nth_element(scores.begin(), scores.begin() + index, scores.end());
        *quantile = scores[index];
    });
}
int32_t eg_conformal_set(const double *probabilities, int32_t count, double quantile, uint32_t *mask) {
    return guard([&] {
        require(probabilities && mask, "Probabilities and output required");
        validate(EG_CHOICE, count, 1, 0);
        require(std::isfinite(quantile) && quantile >= 0 && quantile <= 1, "Quantile must be in [0, 1]");
        double sum = 0; uint32_t value = 0;
        for (int i = 0; i < count; ++i) {
            const double p = probabilities[i];
            require(std::isfinite(p) && p >= 0 && p <= 1, "Probability must be in [0, 1]");
            sum += p;
            if (1 - p <= quantile) value |= uint32_t{1} << i;
        }
        require(std::abs(sum - 1) <= 1e-8, "Probabilities must sum to one");
        *mask = value;
    });
}
}
