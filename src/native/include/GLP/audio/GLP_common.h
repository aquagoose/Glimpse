#ifndef GLIMPSE_COMMON_H
#define GLIMPSE_COMMON_H

#ifdef __cplusplus
extern "C" {
#endif

#define GLP_EXPORT

typedef enum GLP_Result
{
    GLP_RESULT_OK,
    GLP_RESULT_UNKNOWN_ERROR,
    GLP_RESULT_OUT_OF_MEMORY
} GLP_Result;

#ifdef __cplusplus
}
#endif

#endif
