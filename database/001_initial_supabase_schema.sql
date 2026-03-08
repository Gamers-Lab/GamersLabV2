-- Initial schema for GamersLabV2 Supabase/Postgres tables.
-- This script covers the tables referenced by the current API code:
--   public.player_credentials
--   public.error_logs
--   public.write_operations

begin;

create table if not exists public.player_credentials
(
    id serial primary key,
    address text not null,
    private_key text not null,
    immutable_player_identifier text not null,
    created_at timestamptz not null default timezone('utc', now())
);

create unique index if not exists ux_player_credentials_address
    on public.player_credentials (address);

create unique index if not exists ux_player_credentials_immutable_player_identifier
    on public.player_credentials (immutable_player_identifier);

create table if not exists public.error_logs
(
    id serial primary key,
    operation_name text not null,
    error_type text not null,
    error_message text not null,
    stack_trace text null,
    contract_address text null,
    wallet_address text null,
    http_status_code integer null,
    request_path text null,
    user_agent text null,
    client_ip text null,
    created_at timestamptz not null default timezone('utc', now())
);

create index if not exists ix_error_logs_created_at
    on public.error_logs (created_at desc);

create index if not exists ix_error_logs_operation_name
    on public.error_logs (operation_name);

create table if not exists public.write_operations
(
    id serial primary key,
    operation_name text not null,
    contract_address text not null,
    wallet_address text null,
    function_name text not null,
    function_parameters text null,
    payload text null,
    gas_limit bigint null,
    max_fee_per_gas bigint null,
    max_priority_fee_per_gas bigint null,
    status text not null default 'pending',
    attempt_number integer not null default 1,
    is_retry boolean not null default false,
    transaction_hash text null,
    error_message text null,
    send_duration_ms bigint null,
    created_at timestamptz not null default timezone('utc', now()),
    updated_at timestamptz null,
    player_index bigint null check (player_index is null or player_index >= 0)
);

create index if not exists ix_write_operations_created_at
    on public.write_operations (created_at desc);

create index if not exists ix_write_operations_status
    on public.write_operations (status);

create index if not exists ix_write_operations_operation_name
    on public.write_operations (operation_name);

create index if not exists ix_write_operations_transaction_hash
    on public.write_operations (transaction_hash);

create index if not exists ix_write_operations_player_index
    on public.write_operations (player_index);

commit;
