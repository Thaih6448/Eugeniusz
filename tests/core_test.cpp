#include <eugeniusz/eugeniusz.hpp>
#include <atomic>
#include <cmath>
#include <cstring>
#include <iostream>
#include <limits>
#include <thread>
#include <vector>
#define CHECK(x) do { if (!(x)) throw std::runtime_error("Failed: " #x); } while (0)
bool near(double a, double b) { return std::abs(a - b) < 1e-7; }
struct State { std::atomic<int> calls{0}; bool fail = false; };
int32_t backend(void *user, const char *, const eg_question *q, double *out, char *error, uint32_t cap) {
    auto &state = *static_cast<State *>(user);
    ++state.calls;
    if (state.fail && state.calls == 2) { std::snprintf(error, cap, "Expected failure"); return 1; }
    for (int i = 0; i < q->count; ++i) out[i] = i;
    return 0;
}
int main() {
    try {
        eg_result r{};
        double uniform[] = {10000, 10000, 10000};
        CHECK(eg_from_logits(EG_SCORE, uniform, 3, 1, 0.5, &r) == EG_OK);
        CHECK(near(r.value, 1) && near(r.certainty, 0) && r.abstained && r.choice == 0);
        double binary[] = {0, std::log(3.0)};
        CHECK(eg_from_logits(EG_TRUTH, binary, 2, 1, .75, &r) == EG_OK);
        CHECK(near(r.value, .75) && near(r.confidence, .75) && !r.abstained);
        double extreme[] = {-1e100, 1e100};
        CHECK(eg_from_logits(EG_CHOICE, extreme, 2, .05, 1, &r) == EG_OK);
        CHECK(r.probabilities[1] == 1 && r.certainty == 1);
        auto saved = r;
        double bad[] = {0, std::numeric_limits<double>::quiet_NaN()};
        CHECK(eg_from_logits(EG_CHOICE, bad, 2, 1, 0, &r) == EG_INVALID_ARGUMENT);
        CHECK(std::memcmp(&r, &saved, sizeof(r)) == 0);
        CHECK(eg_from_logits(EG_TRUTH, uniform, 3, 1, 0, &r) == EG_INVALID_ARGUMENT);
        CHECK(eg_from_logits(EG_CHOICE, binary, 2, 0, 0, &r) == EG_INVALID_ARGUMENT);
        CHECK(eg_from_logits(EG_CHOICE, binary, 2, 1, 2, &r) == EG_INVALID_ARGUMENT);
        const double logits[] = {4,0, 4,0, 4,0, 4,0};
        const int32_t labels[] = {0,0,0,1};
        double t = 0;
        CHECK(eg_fit_temperature(logits, labels, 4, 2, &t) == EG_OK);
        CHECK(near(t, 4 / std::log(3.0)));
        eg_metrics before{}, after{};
        CHECK(eg_measure(logits, labels, 4, 2, 1, 10, &before) == EG_OK);
        CHECK(eg_measure(logits, labels, 4, 2, t, 10, &after) == EG_OK);
        CHECK(after.nll < before.nll && near(after.accuracy, .75) && after.ece < 1e-7 && near(after.brier, .375));
        double probabilities[] = {.9,.8,.7,.6}; double quantile = 0;
        CHECK(eg_conformal_fit(probabilities, 4, .2, &quantile) == EG_OK && near(quantile,.4));
        CHECK(eg_conformal_fit(probabilities, 4, .01, &quantile) == EG_OK && quantile == 1);
        uint32_t mask = 0;
        double p[] = {.75,.25};
        CHECK(eg_conformal_set(p, 2, .3, &mask) == EG_OK && mask == 1);
        CHECK(eg_conformal_set(p, 2, 1, &mask) == EG_OK && mask == 3);
        CHECK(eg_conformal_set(p, 2, .1, &mask) == EG_OK && mask == 0);
        double invalid_p[] = {.2,.2};
        CHECK(eg_conformal_set(invalid_p, 2, 1, &mask) == EG_INVALID_ARGUMENT);
        State state; eg_engine *raw = nullptr;
        CHECK(eg_engine_create(backend, &state, nullptr, &raw) == EG_OK);
        eugeniusz::Engine engine(raw);
        auto truth = engine.truth("state", "Question?");
        CHECK(truth.count == 2 && truth.value > .7);
        state.calls = 0; state.fail = true;
        eg_question qs[] = {eg_question_default(), eg_question_default()};
        for (auto &q : qs) { q.kind = EG_TRUTH; q.instructions = "Question?"; }
        eg_result outputs[2]{}; outputs[0].value = 123; outputs[1].value = 456;
        CHECK(eg_evaluate_batch(raw, "state", qs, 2, outputs) == EG_BACKEND_ERROR);
        CHECK(outputs[0].value == 123 && outputs[1].value == 456);
        CHECK(std::string(eg_last_error()) == "Expected failure");
        state.calls = 0; qs[1].instructions = nullptr;
        CHECK(eg_evaluate_batch(raw, "state", qs, 2, outputs) == EG_INVALID_ARGUMENT && state.calls == 0);
        state.fail = false;
        std::vector<std::thread> threads;
        for (int i = 0; i < 4; ++i) threads.emplace_back([&] { for (int j = 0; j < 50; ++j) engine.truth("state", "Question?"); });
        for (auto &thread : threads) thread.join();
        CHECK(state.calls == 200);
        CHECK(eg_evaluate(nullptr, "state", qs, outputs) == EG_INVALID_ARGUMENT);
        CHECK(eg_measure(logits, labels, 0, 2, 1, 10, &after) == EG_INVALID_ARGUMENT);
        std::cout << "Core numeric, calibration, conformal, ABI, isolation and concurrency tests passed\n";
    } catch (const std::exception &e) { std::cerr << e.what() << '\n'; return 1; }
}
