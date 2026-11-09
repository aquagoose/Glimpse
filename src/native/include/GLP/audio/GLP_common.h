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
    GLP_RESULT_OUT_OF_MEMORY,

    GLP_RESULT_INDEX_OUT_OF_RANGE,
    GLP_RESULT_UNSUPPORTED_CODEC,
    GLP_RESULT_PLAYBACK_STOPPED
} GLP_Result;

#ifdef __cplusplus
}
#endif

#endif
