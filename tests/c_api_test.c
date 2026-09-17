#include <eugeniusz/eugeniusz.h>
#include <math.h>
#include <stdio.h>
int main(void) {
    double logits[2] = {0, 0};
    eg_result result = {0};
    if (eg_abi_version() != EG_ABI_VERSION || eg_from_logits(EG_TRUTH, logits, 2, 1, 0, &result) != EG_OK || fabs(result.value - 0.5) > 1e-12) {
        fprintf(stderr, "C ABI test failed: %s\n", eg_last_error()); return 1;
    }
    return 0;
}
