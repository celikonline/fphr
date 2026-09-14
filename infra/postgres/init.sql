-- SenseİK PostgreSQL başlangıç betiği (yalnızca ilk oluşturma sırasında çalışır)
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS unaccent;
CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS ltree;
CREATE EXTENSION IF NOT EXISTS vector;

-- Türkçe uyumlu, aksan duyarsız arama için yardımcı fonksiyon
CREATE OR REPLACE FUNCTION f_unaccent(text)
RETURNS text AS $$
  SELECT public.unaccent('public.unaccent', $1)
$$ LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT;

-- Test veritabanı (Testcontainers kullanılmadığında lokal testler için)
SELECT 'CREATE DATABASE senseik_test'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'senseik_test')\gexec
