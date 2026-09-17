#ifndef EUGENIUSZ_H
#define EUGENIUSZ_H
#include <stdint.h>
#if defined(_WIN32) && !defined(EG_STATIC)
# ifdef EG_BUILD
#  define EG_API __declspec(dllexport)
# else
#  define EG_API __declspec(dllimport)
# endif
#elif defined(__GNUC__)
# define EG_API __attribute__((visibility("default")))
#else
# define EG_API
#endif
#ifdef __cplusplus
extern "C" {
#endif
#define EG_MAX_OPTIONS 26
#define EG_ABI_VERSION 1
typedef struct eg_engine eg_engine;
typedef enum eg_status { EG_OK = 0, EG_INVALID_ARGUMENT = 1, EG_BACKEND_ERROR = 2, EG_INTERNAL_ERROR = 3 } eg_status;
typedef enum eg_kind { EG_CHOICE = 0, EG_SCORE = 1, EG_TRUTH = 2 } eg_kind;
/* All strings are null-terminated UTF-8. Criteria are ordered descriptions.
 * Truth uses exactly two criteria in false/true order, or NULL with count 0.
 * Inputs are borrowed for the duration of the call. No field may contain NaN. */
typedef struct eg_question {
    int32_t kind;
    const char *instructions;
    const char *const *criteria;
    int32_t count;
    double temperature;
    double min_confidence;
} eg_question;
typedef struct eg_result {
    int32_t kind;
    int32_t count;
    int32_t choice;
    int32_t abstained;
    double value;      /* Choice: index. Score: expected index. Truth: P(true). */
    double confidence; /* Maximum class probability, NOT a correctness guarantee. */
    double certainty;  /* 1 - entropy / log(class count). */
    double probabilities[EG_MAX_OPTIONS];
    double logits[EG_MAX_OPTIONS];
} eg_result;
typedef struct eg_metrics {
    double nll;
    double brier;
    double ece;
    double accuracy;
} eg_metrics;
/* Backend writes count finite logits. Return nonzero on failure and optionally
 * write a null-terminated error into error_buffer. Never throw across the ABI.
 * Calls on one engine are serialized; callbacks must not reenter that engine. */
typedef int32_t (*eg_logits_fn)(void *user, const char *state, const eg_question *question,
                              double *logits, char *error_buffer, uint32_t error_capacity);
typedef void (*eg_destroy_fn)(void *user);
EG_API uint32_t eg_abi_version(void);
EG_API const char *eg_version(void);
/* Thread-local error, valid until the next status-returning core call. */
EG_API const char *eg_last_error(void);
EG_API eg_question eg_question_default(void);
/* Ownership of user transfers only on success. destroy may be NULL. */
EG_API int32_t eg_engine_create(eg_logits_fn backend, void *user, eg_destroy_fn destroy, eg_engine **out);
EG_API void eg_engine_destroy(eg_engine *engine);
EG_API int32_t eg_evaluate(eg_engine *engine, const char *state, const eg_question *question, eg_result *out);
/* Atomic output: on any failure, every caller-owned result is unchanged.
 * Questions are isolated but evaluated sequentially in this implementation. */
EG_API int32_t eg_evaluate_batch(eg_engine *engine, const char *state, const eg_question *questions, int32_t count, eg_result *out);
EG_API int32_t eg_from_logits(int32_t kind, const double *logits, int32_t count,
                             double temperature, double min_confidence, eg_result *out);
/* Row-major logits [rows, classes], integer labels [rows]. Fit on held-out data.
 * Temperature minimizes NLL over [0.05, 20]. Evaluate on a separate test set. */
EG_API int32_t eg_fit_temperature(const double *logits, const int32_t *labels, int32_t rows, int32_t classes, double *temperature);
EG_API int32_t eg_measure(const double *logits, const int32_t *labels, int32_t rows, int32_t classes,
                         double temperature, int32_t bins, eg_metrics *out);
/* Split conformal score = 1 - P(true class). Returns a finite-sample quantile.
 * Use a separate calibration split after temperature fitting. */
EG_API int32_t eg_conformal_fit(const double *true_probabilities, int32_t count, double alpha, double *quantile);
/* Bit i is set when class i belongs to the prediction set (may be empty). */
EG_API int32_t eg_conformal_set(const double *probabilities, int32_t count, double quantile, uint32_t *mask);
#ifdef __cplusplus
}
#endif
#endif
