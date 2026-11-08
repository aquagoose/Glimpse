cmake_minimum_required(VERSION 4.0)
project(Glimpse LANGUAGES C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 20)

add_library(GlimpseNative SHARED)

add_subdirectory(audio)
target_link_libraries(GlimpseNative PUBLIC GlimpseAudio)